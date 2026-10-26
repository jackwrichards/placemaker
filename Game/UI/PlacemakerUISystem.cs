// Placemaker — UI bindings, exactly as UI/src/contract.ts defines them (group "Placemaker").
//
// Six values, each a ValueBinding<string> carrying JSON (never string[] — that broke mod load in
// PLT; RESEARCH-NOTES rule 22). The UI parses them (JSON.parse). Values are rebuilt only when
// their inputs changed (version stamps) and pushed only when the JSON actually differs
// (ValueBinding compares) — not ~100 bindings per frame like GRG.

using System;
using System.Collections.Generic;
using Colossal.UI.Binding;
using Game.UI;
using Placemaker.Core;
using Placemaker.Core.Tool;
using Placemaker.Game.Tool;
using UnityEngine.Scripting;

namespace Placemaker.Game.UI
{
    public sealed partial class PlacemakerUISystem : UISystemBase
    {
        public const string Group = "Placemaker";

        private PlacemakerToolSystem _tool;
        private ValueBinding<string> _state, _catalog, _design, _summary, _assets, _managed, _sitePlan;
        private string _sitePlanStamp;

        private int _stateStamp = -1;
        private int _statePushFrame = -100;
        private int _designStamp = -1, _designPlanStamp = -1, _designAssetsStamp = -1;
        private int _designPushFrame = -100;
        private string _summaryStamp;
        private int _catalogStamp = -1, _catalogGenerators = -1, _catalogPresets = -1;
        private string _assetsStamp;
        private string _managedStamp;
        private int _frame;
        private readonly SortedSet<string> _requestedCategories = new SortedSet<string>(StringComparer.Ordinal);

        [Preserve]
        protected override void OnCreate()
        {
            base.OnCreate();
            _tool = World.GetOrCreateSystemManaged<PlacemakerToolSystem>();
            UiModels.IconFilter = IconPaths.Fix;

            AddBinding(_state = new ValueBinding<string>(Group, "state", "{}"));
            AddBinding(_catalog = new ValueBinding<string>(Group, "catalog", "{\"layers\":[],\"recipes\":[]}"));
            AddBinding(_design = new ValueBinding<string>(Group, "design", "{\"recipeId\":\"\",\"modified\":false,\"root\":null,\"focus\":\"0\"}"));
            AddBinding(_summary = new ValueBinding<string>(Group, "summary", UiModels.Summary(new SummaryInput()).ToString()));
            AddBinding(_assets = new ValueBinding<string>(Group, "assets", "{}"));
            AddBinding(_managed = new ValueBinding<string>(Group, "managed", "null"));
            AddBinding(_sitePlan = new ValueBinding<string>(Group, "sitePlan", "null"));

            Trigger("toggleTool", () => _tool.Toggle());
            Trigger("togglePolish", () => _tool.TogglePolishTool());   // the polish tool's own button
            Trigger("close", () => _tool.Close());
            Trigger<bool>("setPanelCollapsed", c => _tool.SetPanelCollapsed(c));
            Trigger<string>("goToStage", s => { if (StageMachine.TryParse(s, out var st)) _tool.GoToStage(st); });
            Trigger("primary", () => _tool.Primary());
            Trigger("buildAndClose", () => _tool.BuildAndClose());   // Review's second button (the primary keeps the area)
            Trigger<bool>("polishStamp", yes => _tool.PolishStamp(yes));   // PROTOCOL 25: the painter's Polish / Unpolish
            Trigger<string>("polishTheme", theme => _tool.SetPainterTheme(theme));   // the theme the painter's Polish gives
            Trigger<string, double>("polishOption", (id, v) => _tool.SetPolishOption(id, v));   // the painter card's look rows
            Trigger<string, double>("polishKnob", (id, v) => _tool.SetPolishKnob(id, v));   // a look row of the polish tool
            Trigger<string, string>("polishLook", (what, arg) => _tool.PolishLookAction(what, arg));   // save / saveAs / rename / delete / revert
            Trigger<string, string>("polishZone", (zone, look) => _tool.SetPolishZoneLook(zone, look));   // the City window's zone looks
            Trigger<bool>("polishLive", on => _tool.SetPolishLive(on));   // the Live switch beside Polish
            Trigger<string, string>("polishCity", (what, value) => _tool.PolishCity(what, value));   // the painter card's Whole city rows
            Trigger("undo", () => _tool.Undo());
            Trigger("redo", () => _tool.Redo());
            // Area overhaul: Road nodes is part of Points (corners snap to road nodes); an older panel asking for it gets Points.
            Trigger<string>("setAreaMode", m => { if (SelectionModes.TryParse(m, out var mode)) _tool.SetAreaMode(mode == SelectionMode.Perimeter ? SelectionMode.Draw : mode); });
            Trigger<string, double, double>("setBrush", (shape, size, angle) => _tool.SetBrush(shape == "square", (float)size, (float)angle));
            Trigger<string>("setBrushRules", json => _tool.SetBrushRules(UiModels.ParseBrushRules(json, _tool.BrushRules)));
            Trigger("resetBrushRules", () => _tool.ResetBrushRules());
            Trigger("clearArea", () => _tool.ClearArea());

            Trigger<string>("selectRecipe", id => _tool.SelectRecipe(id));
            Trigger<string>("focusStep", p => _tool.FocusStep(p));
            Trigger<string, string, string>("addLayer", (parent, layer, target) => _tool.AddLayer(parent, layer, target));
            Trigger<string>("removeStep", p => _tool.RemoveStep(p));
            Trigger<string, double>("moveStep", (p, d) => _tool.MoveStep(p, (int)Math.Round(d)));
            Trigger<string, bool>("setStepEnabled", (p, e) => _tool.SetStepEnabled(p, e));
            Trigger<string, string, bool>("setFeature", (p, layer, on) => _tool.SetFeature(p, layer, on));
            Trigger<string, string>("setStepTarget", (p, t) => _tool.SetStepTarget(p, t));
            Trigger<string, string, string>("overrideRegion", (p, region, layer) => _tool.OverrideRegion(p, region, layer));
            Trigger<string, string, string>("setValue", (p, id, json) => _tool.SetValue(p, id, json));
            Trigger<string, string, string>("previewValue", (p, id, json) => _tool.PreviewValue(p, id, json));
            Trigger("endPreview", () => _tool.EndPreview());
            Trigger<string>("resetStep", p => _tool.ResetStep(p));
            Trigger<string, string>("applyPreset", (p, preset) => _tool.ApplyPreset(p, preset));
            Trigger<string, string>("selectPreset", (layer, preset) => _tool.SelectPreset(layer, preset));
            Trigger<string, string>("savePreset", (p, preset) => _tool.SavePreset(p, preset));
            Trigger<string, string>("savePresetAs", (p, name) => _tool.SavePresetAs(p, name));
            Trigger<string, string, string>("renamePreset", (layer, preset, name) => _tool.RenamePreset(layer, preset, name));
            Trigger<string, string>("deletePreset", (layer, preset) => _tool.DeletePreset(layer, preset));
            Trigger("exportPresets", () => _tool.ExportPresets());
            Trigger<string>("randomize", p => _tool.Randomize(p));
            Trigger<string, string>("unlockValue", (p, id) => _tool.UnlockValue(p, id));
            Trigger<string, string>("startMarkers", (p, kind) => _tool.StartMarkers(p, kind));
            Trigger<string, string>("clearMarkers", (p, kind) => _tool.ClearMarkers(p, kind));

            Trigger<string>("saveRecipeAs", name => _tool.SaveRecipeAs(name));
            Trigger<string>("deleteRecipe", id => _tool.DeleteRecipe(id));
            Trigger<string>("saveAsDefault", p => _tool.SaveAsDefault(p));

            Trigger<string>("requestAssets", RequestAssets);

            Trigger<double>("manageGroup", id => _tool.ManageGroup((int)id));
            Trigger<double>("editGroup", id => _tool.EditGroup((int)id));
            Trigger<double>("removeGroup", id => _tool.RemoveGroup((int)id));
            Trigger<double, string>("renameGroup", (id, name) => _tool.RenameGroup((int)id, name));
            Trigger<bool>("setAnarchy", on => _tool.SetAnarchy(on));
            Trigger<double, double, double>("locate", (x, y, z) => _tool.Locate((float)x, (float)y, (float)z));
            Trigger<bool>("acknowledgePermanent", ack => _tool.AcknowledgePermanent(ack));
            Trigger<string, string>("setMarkerKind", (p, kind) => _tool.SetMarkerKind(p, kind));
            Trigger<string, double>("removeMarker", (p, i) => _tool.RemoveMarker(p, (int)Math.Round(i)));
            Trigger<string, double, bool>("setMarkerCrosswalk", (p, i, on) => _tool.SetMarkerCrosswalk(p, (int)Math.Round(i), on));   // i < 0: the entrances placed next
            Trigger<double, bool>("ungroupGroup", (id, on) => _tool.UngroupGroup((int)id, on));   // on = false: group again
            Trigger<bool>("setBuildUngrouped", on => _tool.SetBuildUngrouped(on));
            Trigger<bool>("setKeepArea", on => _tool.SetKeepArea(on));   // Review: Build keeps the area (on) or closes Placemaker
            Trigger<string>("setPreviewMode", m => _tool.SetPreviewMode(m));
            // s17 park stats (docs/proposals/s17.md)
            Trigger<string, string>("setParkStat", (id, json) => _tool.SetParkStat(id, json));
            Trigger<string>("resetParkStat", id => _tool.ResetParkStat(id));
            Trigger<bool>("setParkStatsKeepVanilla", keep => _tool.SetParkStatsKeepVanilla(keep));
            // s22 preview legend (docs/proposals/s22.md)
            Trigger<string>("hoverLegend", id => _tool.HoverLegend(id));
            Trigger<string>("hoverAudit", id => _tool.HoverAudit(id));   // s53: "Left out" reason row hovered ("" = none)
            Trigger<string, bool>("setLegendVisible", (id, on) => _tool.SetLegendVisible(id, on));
            Trigger("resetLegend", () => _tool.ResetLegend());
        }

        // Every trigger is wrapped: an exception from a UI call must never escape into the binding layer.
        private void Trigger(string name, Action a) =>
            AddBinding(new TriggerBinding(Group, name, () => Safe(name, a)));
        private void Trigger<T>(string name, Action<T> a) =>
            AddBinding(new TriggerBinding<T>(Group, name, x => Safe(name, () => a(x))));
        private void Trigger<T1, T2>(string name, Action<T1, T2> a) =>
            AddBinding(new TriggerBinding<T1, T2>(Group, name, (x, y) => Safe(name, () => a(x, y))));
        private void Trigger<T1, T2, T3>(string name, Action<T1, T2, T3> a) =>
            AddBinding(new TriggerBinding<T1, T2, T3>(Group, name, (x, y, z) => Safe(name, () => a(x, y, z))));

        private static void Safe(string name, Action a)
        {
            try { a(); }
            catch (Exception e) { Mod.Log.Error(e, "Placemaker: UI trigger '" + name + "' failed"); }
        }

        [Preserve]
        protected override void OnUpdate()
        {
            base.OnUpdate();
            _frame++;
            try
            {
                PollHotkey();
                _tool.PollBuildWhileInactive();
                _tool.PollGardenPreview();
                // While the tool is closed only "open" changes; refresh rarely.
                if (!_tool.IsActive && _stateStamp == _tool.ViewVersion && _frame % 30 != 0) return;
                PushState();
                PushCatalog();
                PushDesign();
                PushSummary();
                PushSitePlan();
                PushAssets();
                PushManaged();
            }
            catch (Exception e)
            {
                if (_frame % 600 == 1) Mod.Log.Error(e, "Placemaker: UI update failed");
            }
        }

        private void PollHotkey()
        {
            var a = ToolRegistration.ToggleAction;
            if (a == null) return;
            if (!a.shouldBeEnabled) a.shouldBeEnabled = true;
            if (a.WasPerformedThisFrame() && ToolRegistration.InGame) _tool.Toggle();
        }

        private void PushState()
        {
            // Money, planning and hover states change without an edit: also refresh twice a second.
            if (_stateStamp == _tool.ViewVersion && _frame % 30 != 0) return;
            // s15: a brush stroke changes the view every frame (area readout): build the state JSON (hints,
            // stage facts, readiness) at most ~10×/s while it runs; the stroke's end pushes at once.
            if (_tool.Painting && _frame - _statePushFrame < 6) return;
            _statePushFrame = _frame;
            _stateStamp = _tool.ViewVersion;
            _state.Update(UiModels.State(_tool.StateInput()).ToString());
        }

        private void PushCatalog()
        {
            int generators = GeneratorRegistry.All.Count;
            int stamp = _tool.Recipes.Version;
            int presets = _tool.Presets?.Version ?? -1;
            if (stamp == _catalogStamp && generators == _catalogGenerators && presets == _catalogPresets) return;
            _catalogPresets = presets;
            _catalogStamp = stamp;
            _catalogGenerators = generators;
            var entries = new List<RecipeListEntry>();
            foreach (var e in _tool.Recipes.Entries)
            {
                // Make row = single types only (FEEDBACK-2); mixes and type+feature variants stay loadable by id.
                if (!Features.ShowInMakeRow(e.Recipe, e.Custom, GeneratorRegistry.Get)) continue;
                entries.Add(new RecipeListEntry
                {
                    Id = e.Recipe.Id,
                    Name = e.Custom || string.IsNullOrEmpty(e.Recipe.NameKey) ? (string.IsNullOrEmpty(e.Recipe.Name) ? e.Recipe.Id : e.Recipe.Name) : e.Recipe.NameKey,
                    Icon = e.Recipe.Icon,
                    Custom = e.Custom,
                    DescriptionKey = e.DescriptionKey,
                });
            }
            // the neighbourhood-polish painter: picked like a type (Recipes/shelf/neighbourhood.json shows it)
            entries.Add(new RecipeListEntry
            {
                Id = PlacemakerToolSystem.PainterId, Name = "Placemaker.Painter.Name", Icon = "Media/Game/Icons/Lawn.svg",
                DescriptionKey = "Placemaker.Painter.Desc", Painter = true, Group = "neighbourhood",
            });
            var store = _tool.Presets;
            _catalog.Update(UiModels.Catalog(GeneratorRegistry.All, entries, store != null ? g => store.ViewsFor(g) : (Func<IAreaGenerator, IReadOnlyList<PresetView>>)null).ToString());
        }

        private void PushDesign()
        {
            // AssetsResolvedStamp: automatic picks are resolved a frame after the plan lands (resolvedAssets / netLegend names).
            if (_designStamp == _tool.DesignVersion && _designPlanStamp == _tool.PlanRevision && _designAssetsStamp == _tool.AssetsResolvedStamp && _frame % 30 != 0) return;
            // While a slider drags (previewValue) the panel already shows the dragged value: push the design at
            // most every 6th frame so the drag stays smooth; endPreview bumps the version and the final state goes out.
            if (_tool.ValuePreviewActive && _designPlanStamp == _tool.PlanRevision && _frame - _designPushFrame < 6) return;
            _designPushFrame = _frame;
            _designStamp = _tool.DesignVersion;
            _designPlanStamp = _tool.PlanRevision;
            _designAssetsStamp = _tool.AssetsResolvedStamp;
            _design.Update(UiModels.Design(_tool.Painter ? null : _tool.CurrentRecipe, _tool.Painter ? PlacemakerToolSystem.PainterId : _tool.RecipeId, !_tool.Painter && _tool.Modified, _tool.Focus, _tool.StepStatuses(), _tool.Plan,
                null, _tool.ActivePreset, _tool.PresetsFile, _tool.PlanStale, _tool.Unavailability(), AssetLabelOf).ToString());
        }

        // (s16) Prefab id → localised display name + thumbnail, for "Automatic · Two-Lane Road" and the road legend.
        private readonly Dictionary<string, AssetLabel> _labels = new Dictionary<string, AssetLabel>(StringComparer.Ordinal);
        private int _labelsVersion = int.MinValue;

        private AssetLabel AssetLabelOf(string category, string id)
        {
            var catalog = Services.Assets;
            if (string.IsNullOrEmpty(id) || catalog == null || !catalog.Ready) return null;
            if (_labelsVersion != catalog.Version) { _labels.Clear(); _labelsVersion = catalog.Version; }
            string key = (category ?? "") + "|" + id;
            if (_labels.TryGetValue(key, out var hit)) return hit;
            AssetLabel found = null;
            try
            {
                var items = catalog.Items(category ?? "");
                if (items != null)
                    foreach (var it in items)
                        if (it != null && it.Id == id) { found = new AssetLabel { Name = it.DisplayName ?? "", Icon = it.Icon ?? "" }; break; }
            }
            catch (Exception) { found = null; }
            _labels[key] = found;
            return found;
        }

        /// <summary>(sketch study) The site plan for the panel's top view: once per plan (Core/Build/SitePlan.cs ToUi).</summary>
        private void PushSitePlan()
        {
            bool open = _tool.Stage == ToolStage.Design || _tool.Stage == ToolStage.Review || _tool.Stage == ToolStage.Building;
            var sp = open ? _tool.SitePlan : null;
            bool classic = (ToolRegistration.Settings?.SketchStyle ?? global::Placemaker.Game.Settings.SketchStyle.SitePlan) == global::Placemaker.Game.Settings.SketchStyle.Classic;
            string stamp = sp == null ? "none" : _tool.SitePlanVersion + "|" + _tool.PlanStale + "|" + classic;
            if (stamp == _sitePlanStamp) return;
            _sitePlanStamp = stamp;
            if (sp == null || sp.Outline.Length < 3) { _sitePlan.Update("null"); return; }
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var json = sp.ToUi(_tool.SitePlanVersion.ToString());
            json.Set("style", classic ? "classic" : "siteplan");
            if (_tool.PlanStale) json.Set("stale", true);
            string text = json.ToString();
            _sitePlan.Update(text);
            if (sw.ElapsedMilliseconds > 50) Mod.Log.Info($"Placemaker site plan: panel picture sent in {sw.ElapsedMilliseconds} ms ({text.Length / 1024} KB)");
        }

        private void PushSummary()
        {
            bool afford = _tool.CanAfford();
            var failure = _tool.BuildFailure;
            bool ack = _tool.PermanentAcknowledged;
            string stamp = _tool.PlanRevision + "|" + _tool.PlanStale + "|" + afford + "|" + _tool.FeedbackVersion + "|" + (failure?.Key ?? "") + "|"
                + (Services.Assets?.Version ?? -1) + "|" + _tool.AssetsResolvedStamp + "|" + ack + "|" + _tool.GhostDropped + "|" + _tool.ParkStatsVersion + "|" + _tool.DesignVersion + "|" + global::Placemaker.Game.Economy.ParkCompanionSystem.Enabled + "|" + _tool.BuildUngrouped;
            if (stamp == _summaryStamp) return;
            _summaryStamp = stamp;
            var input = new SummaryInput
            {
                Plan = _tool.Plan,
                Recipe = _tool.CurrentRecipe,
                Assets = SafeInfoMap(),
                Stale = _tool.PlanStale || (_tool.Plan == null && _tool.Planning),
                CanAfford = afford,
                PermanentAcknowledged = ack,
                AreaM2 = _tool.Plan != null ? _tool.AreaM2 : 0,
                ParkCompanion = global::Placemaker.Game.Economy.ParkCompanionSystem.Enabled && global::Placemaker.Game.Build.BuildService.ApplyParkStats,
                Ungrouped = _tool.BuildUngrouped,
                AssetLabel = AssetLabelOf,
            };
            if (input.ParkCompanion && _tool.Plan != null)
            {
                _tool.ParkSite(out var outline, out var roads);
                input.Outline = outline;
                input.Roads = roads;
            }
            foreach (var n in _tool.BuildNotes) input.ExtraIssues.Add(n);
            input.Audit = _tool.BuildAudit;
            var clipIssue = _tool.ClipIssue;
            if (clipIssue != null) input.ExtraIssues.Add(clipIssue);
            var ghostDropped = _tool.GhostDroppedIssue;
            if (ghostDropped != null) input.ExtraIssues.Add(ghostDropped);
            if (failure != null)
            {
                input.ExtraIssues.Add(failure);
                input.ExtraIssues.Add(new PlanIssue { Severity = IssueSeverity.Info, Key = "Placemaker.Issue.BuildFailedNext" });
            }
            _summary.Update(UiModels.Summary(input).ToString());
        }

        private static Dictionary<string, AssetInfo> SafeInfoMap()
        {
            try { return Services.Assets?.InfoMap(); } catch (Exception) { return null; }
        }

        private void RequestAssets(string category)
        {
            if (string.IsNullOrEmpty(category)) return;
            _requestedCategories.Add(category);
            _assetsStamp = null;
        }

        private void PushAssets()
        {
            var cat = Services.Assets;
            bool ready = cat != null && cat.Ready;
            string stamp = (ready ? cat.Version : -1) + "|" + _requestedCategories.Count;
            if (stamp == _assetsStamp) return;
            _assetsStamp = stamp;
            var o = JsonValue.Obj();
            if (ready)
            {
                foreach (var category in _requestedCategories)
                {
                    var arr = JsonValue.Arr();
                    IReadOnlyList<CatalogItem> items = null;
                    try { items = cat.Items(category); } catch (Exception) { }
                    if (items != null)
                        foreach (var it in items)
                        {
                            if (it == null) continue;
                            var io = JsonValue.Obj().Set("id", it.Id ?? "").Set("name", string.IsNullOrEmpty(it.DisplayName) ? it.Id ?? "" : it.DisplayName)
                                .Set("icon", it.Icon ?? "");
                            if (it.Tags != null && it.Tags.Length > 0) io.Set("tags", UiModels.Strings(it.Tags));
                            if (it.Info.Cost > 0) io.Set("cost", Math.Round(it.Info.Cost, 2));
                            arr.Add(io);
                        }
                    o.Set(category, arr);
                }
            }
            // Categories missing from the object = still loading (the panel shows "Asset catalog loading…").
            _assets.Update(o.ToString());
        }

        private readonly Dictionary<int, bool> _canEdit = new Dictionary<int, bool>();

        private void PushManaged()
        {
            int id = _tool.ManagedGroup != 0 ? _tool.ManagedGroup : _tool.HoverGroup;
            var groups = Services.Groups;
            bool ungrouped = false;
            try { ungrouped = id != 0 && groups != null && groups.IsUngrouped(id); } catch (Exception) { }
            string stamp = id + "|" + (groups != null) + "|" + ungrouped + "|" + (_frame / 60);
            if (stamp == _managedStamp) return;
            _managedStamp = stamp;
            if (id == 0 || groups == null) { _managed.Update("null"); return; }
            GroupInfo info;
            try { info = groups.Get(id); } catch (Exception) { info = null; }
            if (info == null) { _managed.Update("null"); return; }
            if (!_canEdit.TryGetValue(id, out bool canEdit))
            {
                try { canEdit = groups.GetReceipt(id)?.Recipe != null; } catch (Exception) { canEdit = false; }
                _canEdit[id] = canEdit;
            }
            var o = JsonValue.Obj()
                .Set("id", info.Id)
                .Set("recipeName", new LocText(string.IsNullOrEmpty(info.RecipeNameKey) ? "Placemaker.Recipe.Unknown" : info.RecipeNameKey).ToJson());
            if (!string.IsNullOrEmpty(info.CustomName)) o.Set("customName", info.CustomName);
            o.Set("stats", UiModels.Stats(info.Stats));
            o.Set("upkeep", Math.Round(info.Upkeep));
            o.Set("canEdit", canEdit);
            if (ungrouped) o.Set("ungrouped", true);
            _managed.Update(o.ToString());
        }
    }
}
