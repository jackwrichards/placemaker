// Game runner — the neighbourhood-polish painter and the per-lot flag (GPL-3.0).
//   {"do":"polish","stamp":"yes"}   the area picked with an "area" step is stamped through the panel: the polish tool opened with
//                                   its own button, then Polish (the main button) or Unpolish ("stamp":"no"); waits until
//                                   the polish is idle ("wait": seconds, default 120) and records every lot in the land
//   {"do":"polish","theme":"natural"}            a Style chip (the themes) of the polish tool, clicked
//   {"do":"polish","option":"rocks","value":0}   a Look row of the polish tool (toggles 1/0, "painted" 0-100), clicked
//   {"do":"polish","city":"homes","value":"natural"}  a Whole city row (on, newHouses: "true"/"false"; homes, shops,
//                                   offices, industry: "auto" (each zone type its own look), a look or "off"), clicked
//   {"do":"polish","open":"city"}  a section of the card opened ("look", "city"; "close" to fold it)
//   {"do":"polish","click":"polish-all"}  any element of the panel clicked by its data-testid (a confirm dialog opens)
//   {"do":"polish","clickText":"Polish all"}  the element showing exactly this text clicked (the confirm dialog's button)
//   {"do":"lab","op":"polishFlags"} every growable lot in the land: its flag and what the polish changed on it
//   {"do":"lab","op":"saveLoad"}    the city saved to a scratch save and loaded back (the flags and the edits must survive)
//   {"do":"lab","op":"removeAllPolish"}  the settings button "Remove all polish", then until the polish is idle

using System;
using System.Collections;
using System.Linq;
using Game;
using Game.Common;
using Game.SceneFlow;
using Game.Tools;
using Newtonsoft.Json.Linq;
using Placemaker.Core.Tool;
using Placemaker.Game.Neighbours;
using Placemaker.Game.Tool;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

namespace Placemaker.Game.Suite
{
    public sealed partial class SuiteRunner
    {
        private IEnumerator StepPolish(ScenarioState st, JObject step, JObject rec)
        {
            if (step["theme"] != null || step["knob"] != null || step["option"] != null || step["city"] != null || step["open"] != null || step["close"] != null || step["click"] != null || step["clickText"] != null)
            {
                yield return PolishCard(st, step, rec);
                yield break;
            }
            string stamp = ((string)step["stamp"] ?? "yes").ToLowerInvariant();
            bool yes = stamp != "no";
            bool pickOnly = stamp == "pick";   // {"stamp":"pick"}: the painter picked first, the area painted after (as a player does)
            if (!pickOnly && (!_tool.SuiteHasSelection || _tool.Shape == null)) { rec["error"] = "polish: pick the lots with an area step first"; yield break; }
            // the painter is picked in the Make row, which the panel shows in Design
            // (the toolbar button opens the panel in its Outgrow mode: enough to click its controls)
            if (!PolishOpen) yield return OpenPolishTool(st, rec);
            if (!_tool.Painter && !(pickOnly && _tool.OutgrowMode)) { rec["error"] = "polish: the polish tool did not open"; yield break; }
            if (pickOnly) { rec["painter"] = true; yield break; }
            var (lots, polished) = _tool.PainterCount;
            rec["areaLots"] = lots;
            rec["areaPolishedBefore"] = polished;
            var panel = Bound(rec, "state")?["painter"];
            if (panel != null) rec["panel"] = panel;
            int stamps0 = NeighbourPolishSystem.TotalStampedYes + NeighbourPolishSystem.TotalStampedNo;
            // the selection stays after a stamp: the stamp is seen in the totals
            if (yes)
                yield return UiAct(st, rec, "Polish", "primary", "primary", null, () => NeighbourPolishSystem.TotalStampedYes + NeighbourPolishSystem.TotalStampedNo > stamps0 || _tool.LastStamp.Lots > 0 && _tool.FlashText?.Key == "Placemaker.Painter.Polished", () => _tool.Primary());
            else
                yield return UiAct(st, rec, "Unpolish", "polish-no", "polishStamp", new object[] { false }, () => NeighbourPolishSystem.TotalStampedYes + NeighbourPolishSystem.TotalStampedNo > stamps0 || _tool.FlashText?.Key == "Placemaker.Painter.Unpolished", () => _tool.PolishStamp(false));
            yield return Until(() => NeighbourPolishSystem.TotalStampedYes + NeighbourPolishSystem.TotalStampedNo > stamps0, 5f, "stamp applied");
            rec["stamped"] = _tool.LastStamp.Lots;
            rec["yes"] = yes;
            yield return PolishIdle(rec, (float?)step["wait"] ?? 120f);
            rec["lots"] = PolishLots(st);
            rec["lotsLine"] = _lastLotsLine;
            Mod.Log.Info($"Placemaker suite: {(yes ? "polish" : "unpolish")} stamped on {_tool.LastStamp.Lots} lots; {_lastLotsLine}");
            Event("build");
        }

        /// <summary>The painter card and its Style row, as a person uses them (each control clicked; its trigger if it is not on screen).</summary>
        private IEnumerator PolishCard(ScenarioState st, JObject step, JObject rec)
        {
            // a Zoning type picked in Placemaker: its own panel (the City window, its groups) is clicked as it is
            bool zoningType = _tool.PolishType && _tool.IsActive;
            if (!zoningType && !PolishOpen) yield return OpenPolishTool(st, rec);
            if (!zoningType && !_tool.Painter && !_tool.OutgrowMode) { rec["error"] = "polish: the polish tool did not open"; yield break; }
            var s = ToolRegistration.Settings;
            if (step["theme"] != null)
            {
                string theme = (string)step["theme"];
                yield return UiAct(st, rec, "style " + theme, "polish-theme-" + theme, "polishTheme", new object[] { theme }, () => _tool.PainterTheme == theme, () => _tool.SetPainterTheme(theme));
                rec["theme"] = _tool.PainterTheme;
            }
            if (step["knob"] != null)
            {
                // a setting of the look picked, as its row sets it
                string id = (string)step["knob"];
                double v = (double?)step["value"] ?? 1.0;
                yield return UiAct(st, rec, "look " + id, null, "polishKnob", new object[] { id, v }, () => Math.Abs(_tool.PolishKnobValue(id) - v) < 0.01, () => _tool.SetPolishKnob(id, v));
                rec["value"] = _tool.PolishKnobValue(id);
            }
            if (step["option"] != null && s != null)
            {
                string id = (string)step["option"];
                double v = (double?)step["value"] ?? 1.0;
                Func<double> now = () => id switch
                {
                    "painted" => s.PolishPaintedGardens, "lawns" => s.PolishLawns ? 1 : 0, "planting" => s.PolishPlanting ? 1 : 0, "features" => s.PolishFeatures ? 1 : 0,
                    "paths" => s.PolishPaths ? 1 : 0, "rocks" => s.PolishRocks ? 1 : 0, "groundPaint" => s.PolishGroundPaint ? 1 : 0, "shopPaving" => s.PolishPavedFill ? 1 : 0, _ => -1,
                };
                // a toggle is clicked (it flips); the slider is set through its trigger (a drag)
                string testId = id == "painted" ? null : "polish-" + id;
                yield return UiAct(st, rec, "look " + id, testId, "polishOption", new object[] { id, v }, () => Math.Abs(now() - v) < 0.01, () => _tool.SetPolishOption(id, v));
                rec["value"] = now();
            }
            if (step["city"] != null && s != null)
            {
                string what = (string)step["city"];
                string value = (string)step["value"] ?? "";
                Func<bool> done = what switch
                {
                    "on" => () => NeighbourPolishSystem.Paused == (value != "true"),
                    "newHouses" => () => NeighbourPolishSystem.PolishNew == (value == "true"),
                    "levelUps" => () => NeighbourPolishSystem.PolishLevelUps == (value == "true"),
                    "homes" => () => s.PolishThemeHomes == value,
                    "shops" => () => s.PolishThemeShops == value,
                    "offices" => () => s.PolishThemeOffices == value,
                    "industry" => () => s.PolishThemeIndustry == value,
                    _ => null,
                };
                if (done == null) { rec["error"] = "polish: unknown city row '" + what + "'"; yield break; }
                string testId = what == "on" || what == "newHouses" || what == "levelUps" ? "polish-" + what : "polish-kind-" + what + "-" + value;
                yield return UiAct(st, rec, "city " + what, testId, "polishCity", new object[] { what, value }, done, () => _tool.PolishCity(what, value));
            }
            string open = (string)step["open"] ?? (string)step["close"];
            if (open != null)
            {
                bool want = step["open"] != null;
                JObject reply = null;
                string js = "var el=document.querySelector('[data-testid=\"polish-" + open + "\"]');if(!el)return {found:false};"
                    + "var isOpen=el.getAttribute('data-open')==='true';if(isOpen!==" + (want ? "true" : "false") + "){var b=document.querySelector('[data-testid=\"polish-" + open + "-toggle\"]');if(b)b.click();}"
                    + "return {found:true,was:isOpen};";
                yield return UiRun(js, r => reply = r);
                rec["section"] = reply?["value"];
                if (reply == null || !((bool?)reply["value"]?["found"] ?? false)) rec["error"] = "polish: no section '" + open + "' in the card";
                yield return Frames(6);
            }
            if (step["click"] != null)
            {
                string testId = (string)step["click"];
                JObject reply = null;
                yield return UiRun(SuiteUiSystem.ElementJs(testId, click: true), r => reply = r);
                rec["clicked"] = reply?["value"];
                if (reply == null || !((bool?)reply["value"]?["found"] ?? false)) rec["error"] = "polish: nothing to click with data-testid '" + testId + "'";
                yield return Frames(10);
            }
            if (step["drag"] != null)
            {
                // a window moved by its header: { "drag": testId, "by": [dx, dy] } in screen pixels
                string testId = (string)step["drag"];
                var by = step["by"] as JArray;
                float dx = by != null && by.Count > 0 ? (float)by[0] : 0f, dy = by != null && by.Count > 1 ? (float)by[1] : 0f;
                JObject reply = null;
                yield return UiRun(SuiteUiSystem.DragJs(testId, dx, dy), r => reply = r);
                rec["dragged"] = reply?["value"];
                if (reply == null || !((bool?)reply["value"]?["found"] ?? false)) rec["error"] = "polish: nothing to drag with data-testid '" + testId + "'";
                yield return Frames(10);
            }
            if (step["doubleClick"] != null)
            {
                string testId = (string)step["doubleClick"];
                JObject reply = null;
                yield return UiRun(SuiteUiSystem.DoubleClickJs(testId), r => reply = r);
                if (reply == null || !((bool?)reply["value"]?["found"] ?? false)) rec["error"] = "polish: nothing to double-click with data-testid '" + testId + "'";
                yield return Frames(10);
            }
            if (step["clickText"] != null)
            {
                string text = (string)step["clickText"];
                JObject reply = null;
                // the deepest element showing exactly this text, then its nearest button (the game's own dialog has no test ids)
                // (a trailing * matches the start of the text: a count in a button, "Change *")
                bool prefix = text.EndsWith("*");
                string js = "var want=" + SuiteUiSystem.Q(prefix ? text.Substring(0, text.Length - 1) : text) + ";var pre=" + (prefix ? "true" : "false") + ";var all=document.querySelectorAll('*');var el=null;"
                    + "for(var i=0;i<all.length;i++){var n=all[i];var x=String(n.textContent||'').trim();if(pre?x.indexOf(want)===0:x===want)el=n;}"
                    + "if(!el)return {found:false};var b=el;for(var k=0;k<4&&b&&b.tagName!=='BUTTON';k++)b=b.parentElement;if(!b||b.tagName!=='BUTTON')b=el;"
                    + "try{b.click();}catch(e){var ev=new MouseEvent('click',{bubbles:true,cancelable:true});b.dispatchEvent(ev);}return {found:true,tag:b.tagName};";
                yield return UiRun(js, r => reply = r);
                rec["clicked"] = reply?["value"];
                if (reply == null || !((bool?)reply["value"]?["found"] ?? false)) rec["error"] = "polish: no element shows '" + text + "'";
                yield return Frames(10);
            }
            var panel = Bound(rec, "state")?["painter"];
            if (panel != null) rec["panel"] = panel;
        }

        /// <summary>The painter, or the panel in its Outgrow mode, is open (the button pressed again would close it).</summary>
        private bool PolishOpen => _tool.IsActive && (_tool.Painter || _tool.OutgrowMode);

        /// <summary>The polish tool's own button (next to Placemaker's): its trigger, as the button fires it.</summary>
        private IEnumerator OpenPolishTool(ScenarioState st, JObject rec)
        {
            yield return UiAct(st, rec, "Neighbourhood polish button", null, "togglePolish", null, () => PolishOpen, () => _tool.TogglePolishTool());
            yield return Frames(6);
        }

        /// <summary>Until the polish has nothing waiting (stamps planned, lots put back), then a few frames for the writes.</summary>
        private IEnumerator PolishIdle(JObject rec, float limit)
        {
            float t0 = Time.realtimeSinceStartup;
            yield return Frames(6);
            while ((NeighbourPolishSystem.Busy || NeighbourPolishSystem.Waiting > 0) && Time.realtimeSinceStartup - t0 < limit) yield return null;
            yield return Frames(20);
            rec["polishSeconds"] = Math.Round(Time.realtimeSinceStartup - t0, 1);
            if (NeighbourPolishSystem.Busy) rec["polishStillBusy"] = true;
        }

        /// <summary>Every growable lot in the land, west to east then south to north: flag and what the polish changed.</summary>
        private JArray PolishLots(ScenarioState st, JObject rec = null)
        {
            var sys = _world.GetExistingSystemManaged<NeighbourPolishSystem>();
            var arr = new JArray();
            if (sys == null) return arr;
            var b = st.S.Block;
            var em = _world.EntityManager;
            int yesN = 0, noN = 0, edited = 0, editedNo = 0;
            var line = new System.Text.StringBuilder();
            using (var q = em.CreateEntityQuery(ComponentType.ReadOnly<global::Game.Buildings.Building>(), ComponentType.ReadOnly<global::Game.Objects.Transform>(), ComponentType.ReadOnly<global::Game.Prefabs.PrefabRef>(), ComponentType.Exclude<Deleted>(), ComponentType.Exclude<Temp>()))
            using (var all = q.ToEntityArray(Allocator.Temp))
            {
                var inLand = all.Where(e =>
                {
                    var p = em.GetComponentData<global::Game.Objects.Transform>(e).m_Position;
                    var prefab = em.GetComponentData<global::Game.Prefabs.PrefabRef>(e).m_Prefab;
                    return p.x >= b.X0 && p.x <= b.X1 && p.z >= b.Z0 && p.z <= b.Z1 && em.HasComponent<global::Game.Prefabs.SpawnableBuildingData>(prefab);
                }).OrderBy(e => math.round(em.GetComponentData<global::Game.Objects.Transform>(e).m_Position.x)).ThenBy(e => em.GetComponentData<global::Game.Objects.Transform>(e).m_Position.z).ToList();
                foreach (var e in inLand)
                {
                    var (y, lines, lawns, pieces) = sys.Inspect(e);
                    var p = em.GetComponentData<global::Game.Objects.Transform>(e).m_Position;
                    bool changed = lines + lawns + pieces > 0;
                    if (y) yesN++; else noN++;
                    if (changed) { edited++; if (!y) editedNo++; }
                    line.Append(y ? (changed ? 'Y' : 'y') : (changed ? '!' : 'n'));
                    arr.Add(new JObject
                    {
                        ["id"] = e.Index, ["x"] = Math.Round(p.x, 1), ["z"] = Math.Round(p.z, 1), ["yes"] = y,
                        ["flagged"] = em.HasComponent<PolishedLot>(e), ["linesMoved"] = lines, ["lawnsChanged"] = lawns, ["pieces"] = pieces,
                        ["building"] = em.HasComponent<global::Game.Objects.UnderConstruction>(e),
                    });
                }
            }
            if (rec == null) rec = new JObject();
            _lastLotsLine = $"{yesN} yes, {noN} no, {edited} edited ({editedNo} of them unpolished); lots (y/Y yes untouched/edited, n/! no untouched/EDITED): {line}";
            return arr;
        }

        private string _lastLotsLine = "";

        private IEnumerator LabPolishFlags(ScenarioState st, JObject rec)
        {
            yield return Frames(10);
            rec["lots"] = PolishLots(st);
            rec["lotsLine"] = _lastLotsLine;
            rec["tinted"] = global::Placemaker.Game.Overlay.PlacemakerOverlaySystem.PolishedShown;
            rec["stampedYes"] = NeighbourPolishSystem.TotalStampedYes;
            rec["stampedNo"] = NeighbourPolishSystem.TotalStampedNo;
            rec["restored"] = NeighbourPolishSystem.TotalRestored;
            rec["carried"] = NeighbourPolishSystem.TotalCarried;
            rec["flaggedNew"] = NeighbourPolishSystem.TotalFlaggedNew;
            rec["tagged"] = NeighbourPolishSystem.TotalTagged;
            rec["edgePairs"] = NeighbourPolishSystem.TotalEdgePairs;
            Mod.Log.Info("Placemaker lab: polish flags " + _lastLotsLine + $"; tint shows {rec["tinted"]} lots; {rec["edgePairs"]} pairs against an unpolished lot so far");
        }

        private IEnumerator LabRemoveAllPolish(ScenarioState st, JObject rec)
        {
            int restored0 = NeighbourPolishSystem.TotalRestored;
            NeighbourPolishSystem.RemoveAll();
            yield return PolishIdle(rec, 120f);
            rec["restored"] = NeighbourPolishSystem.TotalRestored - restored0;
            rec["lots"] = PolishLots(st);
            rec["lotsLine"] = _lastLotsLine;
            Mod.Log.Info($"Placemaker lab: remove all polish: {rec["restored"]} lots put back; {_lastLotsLine}");
        }

        /// <summary>The city saved to a scratch save in the player's saves and loaded back (the save is deleted after).</summary>
        private IEnumerator LabSaveLoad(ScenarioState st, JObject rec)
        {
            const string name = "PlacemakerSuitePolish";
            var before = PolishLots(st);
            rec["beforeLine"] = _lastLotsLine;
            var menu = _world.GetExistingSystemManaged<global::Game.UI.Menu.MenuUISystem>();
            if (menu == null || GameManager.instance == null) { rec["error"] = "lab: saveLoad needs the game's menu system"; yield break; }
            System.Threading.Tasks.Task<bool> save = null;
            try { save = GameManager.instance.Save(name, menu.GetSaveInfo(false), Colossal.IO.AssetDatabase.AssetDatabase.user, (global::Game.UI.ScreenCaptureHelper.AsyncRequest)null); }
            catch (Exception e) { rec["error"] = "lab: save failed: " + e.Message; yield break; }
            yield return Until(() => save.IsCompleted, 120f, "save");
            if (!save.IsCompleted || save.IsFaulted) { rec["error"] = "lab: save failed: " + save.Exception?.GetBaseException().Message; yield break; }
            yield return Frames(10);
            var meta = ScratchSave(name);
            if (meta == null) { rec["error"] = "lab: the save was not found after saving"; yield break; }
            float t0 = Time.realtimeSinceStartup;
            Say("loading the scratch save back");
            var load = GameManager.instance.Load(GameMode.Game, Colossal.Serialization.Entities.Purpose.LoadGame, meta);
            yield return Until(() => load.IsCompleted, 600f, "load task");
            yield return Until(() => GameManager.instance.gameMode == GameMode.Game && !GameManager.instance.isGameLoading, 300f, "game mode after load");
            rec["loadSeconds"] = Math.Round(Time.realtimeSinceStartup - t0, 1);
            rec["loaded"] = load.IsCompleted && !load.IsFaulted && load.Result;
            yield return Frames(60);
            _world.GetOrCreateSystemManaged<global::Game.Simulation.SimulationSystem>().selectedSpeed = 0f;
            Event("load");
            yield return OpenTool();
            yield return Frames(30);
            rec["lots"] = PolishLots(st);
            rec["afterLine"] = _lastLotsLine;
            Mod.Log.Info($"Placemaker lab: save and load: before {rec["beforeLine"]}; after {_lastLotsLine}");
            try
            {
                var again = ScratchSave(name);
                if (again != null) SaveHelpers.DeleteSaveGame(again);
            }
            catch (Exception e) { rec["deleteNote"] = "the scratch save stays: " + e.Message; }
        }

        private static global::Game.Assets.SaveGameMetadata ScratchSave(string name)
        {
            foreach (var m in Colossal.IO.AssetDatabase.AssetDatabase.global.GetAssets<global::Game.Assets.SaveGameMetadata>())
            {
                string n;
                try { n = m.name; } catch (Exception) { continue; }
                if (string.Equals(n, name, StringComparison.OrdinalIgnoreCase)) return m;
            }
            return null;
        }
    }
}
