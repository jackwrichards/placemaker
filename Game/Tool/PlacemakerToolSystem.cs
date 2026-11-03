// Placemaker — the one tool: select an area, choose what it becomes, preview, build.
//
// Structure (partials): this file = lifecycle, frame loop, history/snapshots, stage handling;
// .Input = mouse/keyboard helpers and vanilla hint relabelling; .Draw/.Block/.Perimeter/.Paint =
// selection modes; .Markers; .Design = recipe edits from the panel; .Plan = shape rebuild, site
// capture, preview requests, stage facts; .Build = build + manage built groups.
//
// Workflow rules live in Core/Tool/StageMachine (pure, unit-tested); undo in Core/Tool/History.
// Lessons applied from the three source mods: never let an exception escape into the game loop
// (GRG), read modifier clicks from Mouse.current because modifiers mask ProxyActions (PLT, rule 19),
// never iterate all map nodes per frame (GRG's overlay did), snap mask persisted because CS2 resets it (PLT).

using System;
using System.Collections.Generic;
using Game.Common;
using Game.Input;
using Game.Prefabs;
using Game.Tools;
using Placemaker.Core;
using Placemaker.Core.Geometry;
using Placemaker.Core.Tool;
using Placemaker.Game.Preview;
using Placemaker.Game.Recipes;
using Placemaker.Game.Selection;
using Placemaker.Game.Settings;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine.Scripting;

namespace Placemaker.Game.Tool
{
    public sealed partial class PlacemakerToolSystem : ToolBaseSystem
    {
        public const string ToolId = "Placemaker";

        public override string toolID => ToolId;
        public override PrefabBase GetPrefab() => null;
        public override bool TrySetPrefab(PrefabBase prefab) => false;

        // ───────────── collaborators ─────────────

        private PlacemakerSettings _settings;
        private LayerDefaultsStore _defaults;
        private DesignEditor _editor;
        private RecipeStore _recipes;
        private PresetStore _presets;
        private RoadNetwork _roads;
        private SnapCollector _snapCollector;
        private SiteCapture _site;
        private PreviewWorker _worker;
        private readonly SnapResolver _resolver = new SnapResolver();

        // ───────────── live state ─────────────

        private History<ToolSnapshot> _history;
        private SelectionMode _mode;
        private readonly PolygonEditor _draw = new PolygonEditor();
        private readonly BrushMask _paint = new BrushMask();
        private BlockSelectionState _blocks = new BlockSelectionState();
        private PerimeterSelectionState _perimeter = new PerimeterSelectionState();
        private AreaShape _shape;
        private Recipe _recipe;
        private string _recipeId = "";
        private string _focus = RecipePaths.Root;
        private ToolStage _stage = ToolStage.Area;
        private string _markerPath, _markerKind;
        private HashSet<string> _markersEdited = new HashSet<string>(StringComparer.Ordinal);
        private int _editingGroup;
        private bool _panelCollapsed;
        private bool _anarchy;
        private bool _savedIgnoreErrors;
        private bool _anarchyApplied;

        private float _brushSize = 150f;
        private bool _brushSquare;
        private float _brushAngle;

        /// <summary>Short-lived message for the player (refused click, finished build…), shown in hints and tooltips.</summary>
        private LocText _flash;
        private float _flashUntil;

        private bool _initialized;
        private bool _wasRunning;
        private int _errorCount;


        /// <summary>Bumped whenever anything the UI shows may have changed (cheap change detection for bindings).</summary>
        public int ViewVersion { get; private set; }

        public bool IsActive => m_ToolSystem != null && m_ToolSystem.activeTool == this;

        [Preserve]
        protected override void OnCreate()
        {
            base.OnCreate();
            _settings = ToolRegistration.Settings;
            _defaults = ToolRegistration.Defaults ?? new LayerDefaultsStore();
            _editor = new DesignEditor(GeneratorRegistry.Get, id => _defaults.Get(id));
            _recipes = new RecipeStore(() => _editor);
            _presets = new PresetStore(_editor);
            PresetFile.Load(_presets);
            _roads = new RoadNetwork(EntityManager, World.GetOrCreateSystemManaged<global::Game.Net.SearchSystem>());
            _snapCollector = new SnapCollector(EntityManager, World);
            _site = new SiteCapture(EntityManager, World);
            _worker = new PreviewWorker();
            _history = new History<ToolSnapshot>(80);
            CreateHintActions();
            if (_settings != null)
            {
                _brushSize = math.clamp(_settings.BrushSize, BrushMask.MinDiameter, BrushMask.MaxDiameter);
                _brushSquare = _settings.BrushSquare;
                _mode = (SelectionMode)math.clamp(_settings.LastAreaMode, 0, 4);
                // Area overhaul: Road nodes is part of Points now (points snap to road nodes).
                if (_mode == SelectionMode.Perimeter) _mode = SelectionMode.Draw;
                if (_settings.MigrateAreaRules()) _settings.ApplyAndSave();
                _panelCollapsed = _settings.PanelCollapsed;
            }
        }

        [Preserve]
        protected override void OnDestroy()
        {
            _worker?.Dispose();
            base.OnDestroy();
        }

        /// <summary>First open: load recipes (generators register by then) and pick the last used recipe.</summary>
        private void EnsureInitialized()
        {
            if (_initialized) return;
            _initialized = true;
            try
            {
                // Touch the registry on the main thread before any worker does (it initialises lazily).
                _ = GeneratorRegistry.All.Count;
                _recipes.Reload();
            }
            catch (Exception e) { Mod.Log.Error(e, "Placemaker: loading recipes failed"); }
            var last = _settings?.LastRecipe;
            var pick = !string.IsNullOrEmpty(last) && _recipes.Find(last) != null ? last
                     : _recipes.Entries.Count > 0 ? _recipes.Entries[0].Recipe.Id : null;
            // (core) The last type used is shelved now (e.g. Quarry before industry was hidden): start on the first type shown.
            // Only at this restore: a hidden type picked on purpose (the game runner by id, editing an old build) stays.
            if (pick != null && _recipes.IsShelved(pick))
            {
                var shown = _recipes.FirstShown();
                if (shown != null)
                {
                    Mod.Log.Info($"Placemaker: the last type used, '{pick}', is shelved; starting on '{shown}'.");
                    pick = shown;
                }
            }
            if (pick != null)
            {
                _recipe = _recipes.Instantiate(pick, NewSeed());
                _recipeId = pick;
            }
            _history.Reset(Capture());
        }

        public RecipeStore Recipes => _recipes;
        public DesignEditor Editor => _editor;

        public void ReloadRecipes()
        {
            _recipes.Reload();
            Touch();
        }

        // ───────────── activation ─────────────

        public void Toggle()
        {
            // While a build runs the build service owns the tool slot; switching tools would abort it.
            if (m_ToolSystem == null || _building) return;
            if (m_ToolSystem.activeTool == this) Close();
            else { _closeAfterGhost = 0; m_ToolSystem.activeTool = this; }
        }

        public void Close()
        {
            if (_building) return;
            if (m_ToolSystem == null || m_ToolSystem.activeTool != this) return;
            // A visible ghost needs one Clear frame of ours to disappear: hide it and switch a frame later.
            if (BeginGhostHideForClose()) return;
            m_ToolSystem.activeTool = m_DefaultToolSystem;
        }

        [Preserve]
        protected override void OnStartRunning()
        {
            base.OnStartRunning();
            EnsureInitialized();
            RestoreSnapMask();
            EnableActions(true);
            if (_anarchy) ApplyAnarchy(true);
            _wasRunning = true;
            InvalidateFills();   // the city changed while the tool was closed
            Touch();
        }

        [Preserve]
        protected override void OnStopRunning()
        {
            CancelGesture();
            _markerPath = _markerKind = null;
            _hoverGroup = 0;
            _legendHover = "";
            EndValuePreview(true);      // hovers revert, drags commit
            HideGhost();
            _closeAfterGhost = 0;
            EnableActions(false);
            // While the build service holds the tool slot for our build, Anarchy must stay in effect.
            if (_anarchy && !_building) ApplyAnarchy(false);
            LockSnapMask();
            _wasRunning = false;
            Touch();
            base.OnStopRunning();
        }

        public override void InitializeRaycast()
        {
            base.InitializeRaycast();
            if (_stage == ToolStage.Manage)
            {
                m_ToolRaycastSystem.typeMask = TypeMask.Net | TypeMask.StaticObjects | TypeMask.Areas;
                m_ToolRaycastSystem.areaTypeMask = global::Game.Areas.AreaTypeMask.Lots;
                m_ToolRaycastSystem.netLayerMask = global::Game.Net.Layer.All;
                m_ToolRaycastSystem.collisionMask = CollisionMask.OnGround | CollisionMask.Overground;
                return;
            }
            if (_mode == SelectionMode.Perimeter && _markerKind == null && _stage != ToolStage.Building)
            {
                m_ToolRaycastSystem.typeMask = TypeMask.Net;
                m_ToolRaycastSystem.netLayerMask = global::Game.Net.Layer.Road;
                m_ToolRaycastSystem.raycastFlags |= RaycastFlags.SubElements;
                return;
            }
            m_ToolRaycastSystem.typeMask = TypeMask.Terrain;
            m_ToolRaycastSystem.collisionMask = CollisionMask.OnGround;
        }

        // ───────────── frame ─────────────

        [Preserve]
        protected override JobHandle OnUpdate(JobHandle inputDeps)
        {
            // Placemaker never applies: the ghost preview (when shown) needs None to keep its temporaries and asks for a
            // Clear frame only when it replaces them (GhostFrame); the build applies through its own BuildToolSystem.
            applyMode = ApplyMode.None;
            try
            {
                GetAvailableSnapMask(out m_SnapOnMask, out m_SnapOffMask);
                _roads.BeginFrame();
                PumpPreview();
                UpdateBuild();
                ReadInput();

                if (HandleKeys()) { /* key handled: no tool input this frame */ }
                else if (_stage == ToolStage.Building) { /* input ignored while building */ }
                else if (_markerKind != null) UpdateMarkers();
                else if (_stage == ToolStage.Manage) UpdateManage();
                else
                {
                    switch (_mode)
                    {
                        case SelectionMode.Draw: UpdateDraw(); break;
                        case SelectionMode.Block: UpdateBlock(); break;
                        case SelectionMode.Paint: if (BrushLive) UpdatePaint(); else _cursorValid = false; break;
                        case SelectionMode.Perimeter: UpdatePerimeter(); break;
                        case SelectionMode.BlockPaint: if (BrushLive) UpdateBlockPaint(); else { _cursorValid = false; _hoverFace = null; } break;
                    }
                }
                FinishFrame();
            }
            catch (Exception e)
            {
                // Never let a tool bug take the game down: log (rate-limited), abort the gesture, keep the tool usable.
                if (_errorCount++ < 20) Mod.Log.Error(e, "Placemaker: tool update failed");
                try { CancelGesture(); } catch (Exception) { }
            }
            applyMode = GhostFrame();
            return inputDeps;
        }

        private void FinishFrame()
        {
            CheckKeepOffSetting();
            RebuildShapeIfDirty(false);
            UpdatePlanning();
            TickPlanningProgress();
            var facts = Facts();
            var normalized = StageMachine.Normalize(_stage, facts);
            if (normalized != _stage) SetStage(normalized);
            ResolveAssets();
            if (_markerKind != null && (_shape == null || RecipePaths.Resolve(_recipe, _markerPath) == null)) ExitMarkerMode();
            UpdateHintActions();
        }

        // ───────────── stage ─────────────

        public ToolStage Stage => _stage;

        private void SetStage(ToolStage s)
        {
            if (s == _stage) return;
            if (_stage == ToolStage.Manage) { _hoverGroup = 0; }
            _stage = s;
            _legendHover = "";   // s22: a hovered legend entry never outlives its stage
            // Red markers and the failure message describe the attempt the player just saw; moving on clears them (US8).
            ClearBuildFeedback(true);
            if (s != ToolStage.Design && s != ToolStage.Review) ExitMarkerMode();
            Touch();
        }

        /// <summary>UI: jump to a stage if it is reachable now.</summary>
        public bool GoToStage(ToolStage target)
        {
            if (target == ToolStage.Building || !StageMachine.CanGoTo(target, Facts())) return false;
            CancelGesture();
            SetStage(target);
            return true;
        }

        /// <summary>UI button or Enter.</summary>
        public void Primary()
        {
            var facts = Facts();
            var p = StageMachine.Primary(_stage, facts);
            if (!p.Enabled)
            {
                // "Add at least one entrance": pressing anyway starts placing one on that step (FEEDBACK-4b).
                if (p.Kind == PrimaryKind.PlaceMarkers && _markerKind == null && !string.IsNullOrEmpty(p.StepPath))
                {
                    if (_stage == ToolStage.Review) SetStage(ToolStage.Design);   // Entrances live in Design
                    StartMarkers(p.StepPath, p.MarkerKind);
                }
                Flash(p.Reason);
                return;
            }
            switch (p.Kind)
            {
                case PrimaryKind.ToDesign: SetStage(ToolStage.Design); break;
                case PrimaryKind.ToReview: SetStage(ToolStage.Review); break;
                case PrimaryKind.Build:
                case PrimaryKind.FinishEdit: StartBuild(); break;
                case PrimaryKind.PlaceMarkers:
                    if (_markerKind != null) ExitMarkerMode();
                    else StartMarkers(p.StepPath, p.MarkerKind);
                    break;
                case PrimaryKind.CancelBuild: CancelBuild(); break;
            }
        }

        private void Escape()
        {
            switch (StageMachine.Escape(_stage, Facts()))
            {
                case EscapeAction.ExitMarkers: ExitMarkerMode(); break;
                case EscapeAction.CancelGesture: CancelGesture(); break;
                case EscapeAction.ToDesign: SetStage(ToolStage.Design); break;
                case EscapeAction.ToArea: SetStage(ToolStage.Area); break;
                case EscapeAction.ClearSelection: ClearArea(); break;
                case EscapeAction.CloseTool: Close(); break;
            }
        }

        // ───────────── history ─────────────

        private ToolSnapshot Capture()
        {
            return new ToolSnapshot
            {
                Mode = _mode,
                Draw = _draw.Save(),
                Paint = PaintSnapshot(),
                Block = _blocks.Clone(),
                Perimeter = _perimeter.Clone(),
                BlockPaint = BlockPaintSnapshot(),
                Shape = _shape,
                Recipe = _recipe?.Clone(),
                RecipeId = _recipeId ?? "",
                Focus = _focus ?? RecipePaths.Root,
                MarkersEdited = new HashSet<string>(_markersEdited, StringComparer.Ordinal),
                PresetLayer = _presetLayer ?? "",
                PresetId = _presetId ?? "",
            };
        }

        private BrushMask.Snapshot _paintSnap;
        private int _paintSnapVersion = -1;

        private BrushMask.Snapshot PaintSnapshot()
        {
            if (_paint.IsEmpty) return null;
            if (_paintSnap == null || _paintSnapVersion != _paint.Version)
            {
                _paintSnap = _paint.Save();
                _paintSnapVersion = _paint.Version;
            }
            return _paintSnap;
        }

        /// <summary>Records the current live state as one undo step.</summary>
        private void Commit(string labelKey, string coalesceKey = null, params object[] args)
        {
            RebuildShapeIfDirty(true);
            _history.Commit(Capture(), new LocText(labelKey, args), coalesceKey, UnityEngine.Time.realtimeSinceStartup);
            Touch();
        }

        private void Restore(ToolSnapshot s)
        {
            if (s == null) return;
            CancelGesture();
            _mode = s.Mode;
            _draw.Restore(s.Draw);
            if (s.Paint != null) _paint.Restore(s.Paint); else _paint.Clear();
            _paintSnap = s.Paint;
            _paintSnapVersion = _paint.Version;
            _blocks = s.Block?.Clone() ?? new BlockSelectionState();
            _perimeter = s.Perimeter?.Clone() ?? new PerimeterSelectionState();
            RestoreBlockPaint(s.BlockPaint);
            _shape = s.Shape;
            _shapeDirty = false;
            _recipe = s.Recipe?.Clone();
            _parkStatsVersion++;   // undo/redo of a park-stats edit changes no plan input
            _recipeId = s.RecipeId ?? "";
            _focus = s.Focus ?? RecipePaths.Root;
            _markersEdited = s.MarkersEdited != null ? new HashSet<string>(s.MarkersEdited, StringComparer.Ordinal) : new HashSet<string>(StringComparer.Ordinal);
            _presetLayer = s.PresetLayer ?? "";
            _presetId = s.PresetId ?? "";
            ShapeChanged(alreadyBuilt: true);
            DesignChanged();
            _hoverFace = null;
        }

        public bool CanUndo => _history.CanUndo && _gesture == Gesture.None && _stage != ToolStage.Building;
        public bool CanRedo => _history.CanRedo && _gesture == Gesture.None && _stage != ToolStage.Building;
        public LocText UndoLabel => _history.UndoLabel;
        public LocText RedoLabel => _history.RedoLabel;

        public void Undo()
        {
            if (!CanUndo) return;
            EndValuePreview(false);
            Restore(_history.Undo());
        }

        public void Redo()
        {
            if (!CanRedo) return;
            EndValuePreview(false);
            Restore(_history.Redo());
        }

        // ───────────── area mode / clear ─────────────

        public SelectionMode Mode => _mode;

        public void SetAreaMode(SelectionMode mode)
        {
            if (mode == _mode) return;
            CancelGesture();
            ExitMarkerMode();
            // Switching mode keeps the current area until the new mode produces one: the player can
            // look at block picking without losing a drawn outline (undo restores either way).
            _mode = mode;
            _hoverFace = null;
            _hoverNode = Entity.Null;
            if (_settings != null) { _settings.LastAreaMode = (int)mode; _settings.ApplyAndSave(); }
            if (_stage == ToolStage.Manage) SetStage(ToolStage.Area);
            Touch();
        }

        public void ClearArea()
        {
            if (!HasSelection()) return;
            CancelGesture();
            ExitMarkerMode();
            ClearSelectionState();
            // Markers belong to the old outline.
            if (_recipe != null) foreach (var kv in RecipePaths.Walk(_recipe)) kv.Value.Markers.Clear();
            _markersEdited.Clear();
            DesignChanged();
            Commit("Placemaker.Undo.ClearArea");
            SetStage(ToolStage.Area);
        }

        private void ClearSelectionState()
        {
            _draw.Clear();
            _paint.Clear();
            _blocks = new BlockSelectionState();
            _perimeter = new PerimeterSelectionState();
            ClearBlockPaint();
            _shape = null;
            _shapeDirty = false;
            _hoverFace = null;
            ShapeChanged(alreadyBuilt: true);
        }

        private bool HasSelection() =>
            _shape != null || !_draw.IsEmpty || !_paint.IsEmpty || _blocks.Seeds.Count > 0 || _perimeter.Nodes.Count > 0 || !_bpMask.IsEmpty;

        public void SetBrush(bool square, float size, float angle)
        {
            _brushSquare = square;
            _brushSize = math.clamp(size, BrushMask.MinDiameter, BrushMask.MaxDiameter);
            _brushAngle = ((angle % 360f) + 360f) % 360f;
            if (_settings != null) { _settings.BrushSize = _brushSize; _settings.BrushSquare = _brushSquare; _settings.ApplyAndSave(); }
            Touch();
        }

        public void SetPanelCollapsed(bool collapsed)
        {
            _panelCollapsed = collapsed;
            if (_settings != null) { _settings.PanelCollapsed = collapsed; _settings.ApplyAndSave(); }
            Touch();
        }

        // ───────────── anarchy ─────────────

        private static bool? s_AnarchyAvailable;

        /// <summary>Anarchy (yenyang) is installed: it lets players build despite errors, so we offer its toggle.</summary>
        public bool AnarchyAvailable
        {
            get
            {
                if (s_AnarchyAvailable.HasValue) return s_AnarchyAvailable.Value;
                bool found = false;
                try
                {
                    foreach (var a in AppDomain.CurrentDomain.GetAssemblies())
                        if (a.GetName().Name.IndexOf("Anarchy", StringComparison.OrdinalIgnoreCase) >= 0) { found = true; break; }
                }
                catch (Exception) { }
                s_AnarchyAvailable = found;
                return found;
            }
        }

        public bool AnarchyEnabled => _anarchy;

        public void SetAnarchy(bool enabled)
        {
            if (!AnarchyAvailable || enabled == _anarchy) return;
            _anarchy = enabled;
            if (IsActive) ApplyAnarchy(enabled);
            Touch();
        }

        // ToolSystem.ignoreErrors is what Anarchy itself toggles; we only hold it while our tool is active.
        private void ApplyAnarchy(bool on)
        {
            if (m_ToolSystem == null) return;
            if (on == _anarchyApplied) return;
            _anarchyApplied = on;
            if (on) { _savedIgnoreErrors = m_ToolSystem.ignoreErrors; m_ToolSystem.ignoreErrors = true; }
            else m_ToolSystem.ignoreErrors = _savedIgnoreErrors;
        }

        // ───────────── misc ─────────────

        private void Flash(LocText text, float seconds = 4f)
        {
            if (text == null) return;
            _flash = text;
            _flashUntil = UnityEngine.Time.realtimeSinceStartup + seconds;
            Touch();
        }

        public LocText FlashText => _flash != null && UnityEngine.Time.realtimeSinceStartup < _flashUntil ? _flash : null;

        private void Touch() => ViewVersion++;

        private static uint _seedCounter;
        private static uint NewSeed() => Seeds.Mix(SuiteSeed ?? (uint)Environment.TickCount, ++_seedCounter);

        public bool PanelCollapsed => _panelCollapsed;
        public float BrushSize => _brushSize;
        public bool BrushSquare => _brushSquare;
        public float BrushAngle => _brushAngle;
        public int EditingGroup => _editingGroup;
        public string MarkerPath => _markerPath;
        public string MarkerKind => _markerKind;
        public AreaShape Shape => _shape;
        public Recipe CurrentRecipe => _recipe;
        public string RecipeId => _recipeId;
        public string Focus => _focus;
        public bool WasRunning => _wasRunning;
    }
}
