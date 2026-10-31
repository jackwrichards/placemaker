// Shape rebuild, preview planning and the facts the stage machine decides on.

using System;
using System.Collections.Generic;
using Game.City;
using Game.Simulation;
using Placemaker.Core;
using Placemaker.Core.Geometry;
using Placemaker.Core.Tool;
using Placemaker.Game.Settings;
using Unity.Entities;
using Unity.Mathematics;

namespace Placemaker.Game.Tool
{
    public sealed partial class PlacemakerToolSystem
    {
        private bool _shapeDirty, _shapeThrottled;
        private float _lastShapeBuild = -10f;
        private int _shapeVersion;
        private int _inputRevision, _requestedRevision = -1, _planRevision = -1;
        private float _lastInputChange;
        private ComposedPlan _plan;
        private double _planMs;
        private SiteContext _siteCache;
        private int _siteShapeVersion = -1;
        private DesignEditor.ReadinessResult _unready;
        private int _unreadyRevision = -1;
        private int _affordRevision = -1;
        private bool _canAfford = true;
        private double _planCost, _planUpkeep;

        public ComposedPlan Plan => _plan;
        public int PlanRevision => _planRevision;
        public int InputRevision => _inputRevision;
        public int ShapeVersion => _shapeVersion;
        public bool PlanStale => _plan != null && _planRevision != _inputRevision;
        public double PlanMilliseconds => _planMs;
        public double PlanCost => _planCost;
        public double PlanUpkeep => _planUpkeep;

        /// <summary>(Phase 1) A type that needs an entrance has none yet: it plans nothing until one is placed.</summary>
        public bool WaitsForEntrance => _shape != null && _recipe != null && !string.IsNullOrEmpty(Unready()?.MarkerKind);

        public bool Planning
        {
            get
            {
                if (_shape == null || _recipe == null || _painter || _stage == ToolStage.Building) return false;
                if (WaitsForEntrance) return _worker.Busy;   // (Phase 1) nothing is planned until there is an entrance
                return _worker.Busy || _planRevision != _inputRevision;
            }
        }

        private void ShapeChanged(bool throttled = false, bool alreadyBuilt = false)
        {
            if (alreadyBuilt) { _shapeVersion++; UpdateBuildingsInSelection(); }
            else { _shapeDirty = true; _shapeThrottled = throttled; }
            _inputRevision++;
            _lastInputChange = UnityEngine.Time.realtimeSinceStartup;
            ClearBuildFeedback(false);
            Touch();
        }

        /// <summary>Recomputes the AreaShape from the active mode's selection (throttled during drags).</summary>
        private void RebuildShapeIfDirty(bool force)
        {
            if (!_shapeDirty) return;
            float now = UnityEngine.Time.realtimeSinceStartup;
            // s15 (paint lag): while a brush stroke runs the overlay draws the painted cells directly, so the
            // outline — marching squares + smoothing, Block paint's booleans/offsets, keep-off-roads corridor
            // booleans, the attachment and marker searches (each with a net query) — waits for mouse-up.
            // It used to run ~7×/s during the drag (tens to hundreds of ms each on big areas).
            if (!force && Painting && (_mode == SelectionMode.Paint || _mode == SelectionMode.BlockPaint)) return;
            if (!force && _shapeThrottled && now - _lastShapeBuild < 0.15f) return;
            // Paint area: the outline is worked out on a worker first (a big area took 0.2–0.5 s here at mouse-up)
            if (!force && _mode == SelectionMode.BlockPaint && !BlockPaintOutlineReady()) return;
            _shapeDirty = false;
            _lastShapeBuild = now;
            AreaShape s = null;
            try
            {
                switch (_mode)
                {
                    case SelectionMode.Draw: s = BuildDrawShape(); break;
                    case SelectionMode.Block: s = BuildBlockShape(); break;
                    case SelectionMode.Perimeter: s = BuildPerimeterShape(); break;
                    case SelectionMode.Paint: s = BuildPaintShape(); break;
                    case SelectionMode.BlockPaint: s = BuildBlockPaintShape(); break;
                }
            }
            catch (Exception e)
            {
                Mod.Log.Warn("Placemaker: outline rebuild failed: " + e.Message);
                s = null;
            }
            s = KeepOffRoads(s);
            s = CutByStops(s);
            if (s != null && (_mode == SelectionMode.Draw || _mode == SelectionMode.Perimeter) && s.Source != AreaSource.Block)
            {
                // Points along the roads (corners on their nodes) give a city block, like the bucket: no perimeter road, anchors.
                if (RoadBound(s.Ring2D())) s.Source = AreaSource.Block;
                WithFoundAttachments(s);
                AddNodeAttachments(s);
            }
            var old = _shape;
            _shape = s;
            _shapeVersion++;
            FollowOutline(old, s);
            UpdateBuildingsInSelection();
            Touch();
        }

        // ───────────── keep off existing roads (AreaClip) ─────────────

        private AreaClipResult _clip;
        private bool _keepOffApplied = true;

        /// <summary>Parts of the selection left out because they lie on existing roads (for the overlay), or null.</summary>
        public IReadOnlyList<float3[]> ClippedAway => _leftOut != null && _leftOut.Count > 0 && _shape != null ? _leftOut
            : _clip != null && _clip.Clipped && ReferenceEquals(_clip.Shape, _shape) ? _clip.Removed : null;

        /// <summary>Info about the trimmed outline for the summary, or null.</summary>
        public PlanIssue ClipIssue => _clip != null && _clip.Clipped && ReferenceEquals(_clip.Shape, _shape) ? _clip.Issue : null;

        private AreaShape KeepOffRoads(AreaShape s)
        {
            _keepOffApplied = KeepOffRoadsWanted();
            _clip = null;
            if (s == null || !_keepOffApplied || !AreaClip.ClipsSource(s.Source)) return s;
            try
            {
                _clip = AreaClip.Apply(s, _site.NetsAround(s.Ring), p => _site.SampleHeight(p));
                return _clip.Shape ?? s;
            }
            catch (Exception e)
            {
                Mod.Log.Warn("Placemaker: keep-off-roads clip failed: " + e.Message);
                _clip = null;
                return s;
            }
        }

        /// <summary>Re-checks the setting (toggled in the options while the tool is open).</summary>
        /// <summary>
        /// (s27) The brushes follow the shared brush rules ("Roads" stop, "Paint over anything"); Draw keeps the
        /// "Keep off existing roads" option.
        /// </summary>
        // Area overhaul: only plain Paint (Smart edges off) still trims by road corridors here; every other tool stops at the
        // exact road surfaces itself (AreaWalls), so a second, rounder cut would only nick the outline.
        private bool KeepOffRoadsWanted() => _mode == SelectionMode.Paint && BrushRules.Stops(BrushObstacleKind.Road);

        private void CheckKeepOffSetting()
        {
            bool want = KeepOffRoadsWanted();
            if (want != _keepOffApplied && _shape != null && !_shapeDirty) { _keepOffApplied = want; ShapeChanged(); }
        }

        /// <summary>Markers follow the edited outline (MarkerFollow); part of the same undo step as the outline edit.</summary>
        private void FollowOutline(AreaShape old, AreaShape now)
        {
            if (old == null || now == null || _recipe == null || ReferenceEquals(old, now)) return;
            bool changed;
            try { changed = MarkerFollow.Reproject(_recipe, old.Ring, now.Ring, _markersEdited, () => _site.NetsAround(now.Ring)); }
            catch (Exception e) { Mod.Log.Warn("Placemaker: markers could not follow the outline: " + e.Message); return; }
            if (changed) _designVersion++;
        }

        private AreaShape BuildDrawShape()
        {
            if (!_draw.Closed) return null;
            // Area overhaul: stretches between two corners on road nodes follow the road (Points.cs); the stops cut it after.
            var ring = RoutedRing();
            if (ring.Length < 3) return null;
            for (int i = 0; i < ring.Length; i++) ring[i].y = _site.SampleHeight(ring[i].xz);
            return new AreaShape { Ring = ring, Source = AreaSource.Drawn };
        }

        private void WithFoundAttachments(AreaShape shape)
        {
            try { shape.Attachments = AttachmentFinder.Find(shape.Ring, _site.NetsAround(shape.Ring)); }
            catch (Exception e) { Mod.Log.Warn("Placemaker: attachments failed: " + e.Message); }
        }

        // ───────────── preview ─────────────

        private void PumpPreview()
        {
            var r = _worker.Pump();
            if (r == null) return;
            if (r.Revision != _requestedRevision) return;
            _plan = r.Plan;
            _planRevision = r.Revision;
            _planMs = r.Milliseconds;
            _affordRevision = -1;
            var sw = System.Diagnostics.Stopwatch.StartNew();
            if (r.AfterMs > 0 && Services.Assets != null && Services.Assets.Version == _requestedCatalogVersion)
            {
                // resolved on the worker against this very catalog: nothing left to do here
                _resolvedPlan = _plan;
                _resolvedCatalogVersion = _requestedCatalogVersion;
                _assetsResolvedStamp++;
            }
            ResolveAssets();
            // (zoning speed) Where the plan's time went: the worker's steps (Core PlanClock), the site capture before it and the
            // assets after it (main thread). Every plan but those of a running drag (unless slow).
            if (_gesture == Gesture.None || r.Milliseconds >= 100)
                Mod.Log.Info($"Placemaker: plan {_recipe?.Root?.Layer} r{r.Revision}: {r.Milliseconds:0} ms on the worker (site capture {_captureMs:0} ms, assets {sw.Elapsed.TotalMilliseconds:0} ms here + {r.AfterMs:0} ms on the worker), "
                    + $"{CountZoning(_plan)} zone cells: {PlanTimings.Describe(_plan)}");
            Touch();
        }

        private double _captureMs;
        private int _requestedCatalogVersion = -1;

        private static int CountZoning(ComposedPlan plan)
        {
            int n = 0;
            if (plan != null) foreach (var l in plan.Layers) n += l.Zoning.Count;
            return n;
        }

        private void UpdatePlanning()
        {
            bool canPlan = _shape != null && _recipe != null && _stage != ToolStage.Building && !_painter;   // the painter plans nothing
            // (Phase 1) A type that needs an entrance plans nothing until it has one: no long load that ends in "add an entrance"
            // (the user: "we shouldn't start to build it before it has those requirements"). The panel's yellow button leads there.
            if (canPlan && WaitsForEntrance) canPlan = false;
            if (!canPlan)
            {
                if (_plan != null || _requestedRevision >= 0)
                {
                    _worker.Cancel();
                    _plan = null;
                    _requestedRevision = -1;
                    _planRevision = -1;
                    Touch();
                }
                return;
            }
            // The Layout size / Edge margin options changed in the Options menu: plan again.
            var layout = LayoutOptions();
            if (layout != _layoutPlanned)
            {
                if (_layoutPlanned.scale > 0f) { _inputRevision++; _lastInputChange = UnityEngine.Time.realtimeSinceStartup; }
                _layoutPlanned = layout;
            }
            if (_shapeDirty || _planRevision == _inputRevision || _requestedRevision == _inputRevision) return;

            // Committed edits plan at once; only a running drag (or a hover) is debounced (Core/Tool/PlanSchedule).
            var detail = _settings?.PreviewDetail ?? PreviewDetail.Normal;
            float debounce = PlanSchedule.Debounce(_gesture != Gesture.None, ValuePreviewKind, detail == PreviewDetail.Simple, detail == PreviewDetail.Detailed);
            if (debounce < 0f) return;   // Simple detail: re-plan when the drag ends
            if (debounce > 0f && UnityEngine.Time.realtimeSinceStartup - _lastInputChange < debounce) return;

            var capture = System.Diagnostics.Stopwatch.StartNew();
            var site = SiteFor(_shape);
            _captureMs = capture.Elapsed.TotalMilliseconds;
            _requestedRevision = _inputRevision;
            _planStartedAt = UnityEngine.Time.realtimeSinceStartup;
            // (perf) assets resolved on the worker, from the catalog version of now (PumpPreview checks it is still current)
            var assets = Services.Assets as Placemaker.Game.Assets.AssetCatalogSystem;
            var source = assets?.WorkerSource();
            _requestedCatalogVersion = assets?.Version ?? -1;
            _worker.Request(_inputRevision, _recipe.Clone(), _shape, site, source == null ? null : (Action<ComposedPlan>)(p => assets.ResolveAutomatic(p, source)));
            Touch();
        }

        private (float scale, float margin) _layoutPlanned = (-1f, -1f);

        /// <summary>Options "Layout size" (fraction) and "Edge margin" (design metres) handed to the planner via SiteContext.</summary>
        private (float scale, float margin) LayoutOptions() => _settings == null
            ? (SiteContext.DefaultLayoutScale, SiteContext.DefaultEdgeMargin)
            : (_settings.LayoutSize / 100f, _settings.EdgeMargin);

        private float _planStartedAt = -1f;
        private float _planningTouchAt;

        /// <summary>While planning, refresh the state ~10x a second so the spinner's coarse progress moves.</summary>
        private void TickPlanningProgress()
        {
            if (!Planning) return;
            float now = UnityEngine.Time.realtimeSinceStartup;
            if (now - _planningTouchAt < 0.1f) return;
            _planningTouchAt = now;
            Touch();
        }

        /// <summary>Coarse 0..0.95 while Planning (0 = queued, the request has not started), else null.</summary>
        public double? PlanningProgress
        {
            get
            {
                if (!Planning) return null;
                bool running = _requestedRevision == _inputRevision && _worker.Busy;
                double ms = running ? (UnityEngine.Time.realtimeSinceStartup - _planStartedAt) * 1000.0 : -1;
                return PlanSchedule.Progress(ms, _planMs);
            }
        }

        private SiteContext SiteFor(AreaShape shape)
        {
            // Re-capture only when the outline changed (or the catalog delivered new asset info).
            bool assetsWereReady = _site.AssetsReady;
            _site.Assets();
            var (scale, margin) = LayoutOptions();
            if (_siteCache != null && _siteShapeVersion == _shapeVersion && assetsWereReady == _site.AssetsReady)
            {
                // Layout size / edge margin options: a fresh copy (the worker may still read the old one).
                if (_siteCache.LayoutScale != scale || _siteCache.EdgeMargin != margin) _siteCache = _siteCache.WithLayout(scale, margin);
                return _siteCache;
            }
            try { _siteCache = _site.Capture(shape); }
            catch (Exception e)
            {
                Mod.Log.Warn("Placemaker: site capture failed: " + e.Message);
                _siteCache = new SiteContext();
            }
            _siteCache.LayoutScale = scale;
            _siteCache.EdgeMargin = margin;
            _siteShapeVersion = _shapeVersion;
            return _siteCache;
        }

        // ───────────── facts ─────────────

        public LocText AreaProblem()
        {
            if (_mode == SelectionMode.Draw && _draw.Closed)
            {
                var p = _draw.Problem();
                if (p != null) return new LocText(p);
            }
            if (_shape == null)
            {
                if (_clipCoveredAll) return new LocText("Placemaker.Problem.OnOtherBuild");
                if (_mode == SelectionMode.Paint && !_paint.IsEmpty) return new LocText("Placemaker.Problem.PaintTooSmall");
                if (_mode == SelectionMode.BlockPaint && !_bpMask.IsEmpty) return new LocText("Placemaker.Problem.PaintTooSmall");
                return null;
            }
            var gen = _recipe != null ? GeneratorRegistry.Get(_recipe.Root.Layer) : null;
            if (gen != null)
            {
                double area = Pricing.PolygonArea(_shape.Ring);
                try
                {
                    if (gen.MinArea > 0 && area < gen.MinArea) return new LocText("Placemaker.Problem.TooSmallFor", Math.Round(gen.MinArea));
                    if (gen.MaxArea > 0 && area > gen.MaxArea) return new LocText("Placemaker.Problem.TooLargeFor", Math.Round(gen.MaxArea));
                }
                catch (Exception) { }
            }
            return null;
        }

        private List<DesignEditor.ReadinessResult> _unreadyAll = new List<DesignEditor.ReadinessResult>();

        private DesignEditor.ReadinessResult Unready()
        {
            if (_unreadyRevision == _inputRevision) return _unready;
            // s15: the outline does not change during a brush stroke (rebuilt on mouse-up): no readiness checks per frame.
            if (Painting && _unreadyRevision >= 0 && (_mode == SelectionMode.Paint || _mode == SelectionMode.BlockPaint)) return _unready;
            _unreadyRevision = _inputRevision;
            _unreadyAll = _shape != null && _recipe != null ? _editor.AllUnready(_recipe, _shape, _siteCache) : new List<DesignEditor.ReadinessResult>();
            _unready = _unreadyAll.Count > 0 ? _unreadyAll[0] : null;
            return _unready;
        }

        /// <summary>Readiness per step path for the design view (missing = ready).</summary>
        private Dictionary<string, Dictionary<string, LocText>> _unavailable = new Dictionary<string, Dictionary<string, LocText>>();
        private int _unavailableRevision = -1;

        /// <summary>
        /// (s15) Controls / features that do not apply to the current AREA, with the reason (Core/Tool/Availability):
        /// sent with the design so the panel greys them out instead of hiding them. Recomputed when the inputs
        /// change, never during a brush stroke.
        /// </summary>
        public Dictionary<string, Dictionary<string, LocText>> Unavailability()
        {
            if (_unavailableRevision == _inputRevision) return _unavailable;
            if (Painting && _unavailableRevision >= 0) return _unavailable;
            _unavailableRevision = _inputRevision;
            try { _unavailable = Availability.For(_recipe, _shape, _siteCache, GeneratorRegistry.Get); }
            catch (Exception e) { Mod.Log.Warn("Placemaker: availability check failed: " + e.Message); _unavailable = new Dictionary<string, Dictionary<string, LocText>>(); }
            return _unavailable;
        }

        public Dictionary<string, UiModels.StepStatus> StepStatuses()
        {
            Unready();
            var d = new Dictionary<string, UiModels.StepStatus>(StringComparer.Ordinal);
            foreach (var u in _unreadyAll)
                d[u.StepPath] = new UiModels.StepStatus { Ready = false, NextStep = LocText.Of(u.NextStepKey) ?? LocText.Of(u.ReasonKey, u.ReasonArgs) };
            return d;
        }

        public bool CanAfford()
        {
            if (_plan == null) return true;
            if (_affordRevision == _planRevision) return _canAfford;
            _affordRevision = _planRevision;
            var est = Pricing.Of(_plan, _site.Assets());
            _planCost = est.Cost;
            _planUpkeep = est.Upkeep;
            _canAfford = true;
            try
            {
                var city = World.GetExistingSystemManaged<CitySystem>();
                if (city != null && city.City != Entity.Null && EntityManager.HasComponent<PlayerMoney>(city.City))
                {
                    var money = EntityManager.GetComponentData<PlayerMoney>(city.City);
                    _canAfford = money.m_Unlimited || money.money >= est.Cost;
                }
            }
            catch (Exception) { _canAfford = true; }
            return _canAfford;
        }

        public StageFacts Facts()
        {
            var f = new StageFacts
            {
                AreaValid = _shape != null,
                HasSelection = HasSelection(),
                AreaInProgress = (_mode == SelectionMode.Draw && !_draw.Closed && _draw.Count > 0)
                                 || (_mode == SelectionMode.Perimeter && !_perimeter.Closed && _perimeter.Nodes.Count > 0),
                AreaProblem = AreaProblem(),
                HasRecipe = _recipe != null,
                Planning = Planning,
                PlanReady = _plan != null && _planRevision == _inputRevision,
                PlanHasErrors = _plan != null && _plan.HasErrors,
                CanAfford = CanAfford(),
                PermanentPending = PermanentPending(),
                BuildServiceAvailable = Services.Build != null,
                BuildRunning = _building,
                MarkerModeActive = _markerKind != null,
                Editing = _editingGroup > 0,
                GestureActive = _gesture != Gesture.None || _previewBase != null,
                Painter = _painter,
            };
            var u = Unready();
            if (u != null)
            {
                f.UnreadyStepPath = u.StepPath;
                f.UnreadyMarkerKind = u.MarkerKind;
                f.UnreadyNextStep = LocText.Of(u.NextStepKey);
                f.UnreadyReason = LocText.Of(u.ReasonKey, u.ReasonArgs);
            }
            return f;
        }

        /// <summary>Area of the current outline in m² (live while dragging a drawn outline).</summary>
        public double AreaM2
        {
            get
            {
                if (_mode == SelectionMode.Draw && _draw.Closed) return _draw.Area();
                // s15: while a stroke runs the outline is stale (rebuilt on mouse-up): show the painted cells.
                if (Painting && _mode == SelectionMode.Paint) return _paint.Area;
                if (Painting && _mode == SelectionMode.BlockPaint) return _bpMask.Area;
                if (_shape != null) return Pricing.PolygonArea(_shape.Ring);
                if (_mode == SelectionMode.Paint) return _paint.Area;
                if (_mode == SelectionMode.BlockPaint) return _bpMask.Area;
                return 0;
            }
        }

        public double PerimeterM
        {
            get
            {
                if (_mode == SelectionMode.Draw && _draw.Count > 0) return _draw.Perimeter();
                if (_shape != null) return Poly.Perimeter(_shape.Ring2D());
                return 0;
            }
        }

        public bool AreaClosed
        {
            get
            {
                switch (_mode)
                {
                    case SelectionMode.Draw: return _draw.Closed;
                    // s43: an open chain of 3+ nodes closes itself (preview), so it is an area already.
                    case SelectionMode.Perimeter: return _perimeter.Closed || _shape != null;
                    case SelectionMode.Block: return _blocks.Seeds.Count > 0;
                    default: return _shape != null;
                }
            }
        }

        public int PointCount
        {
            get
            {
                switch (_mode)
                {
                    case SelectionMode.Draw: return _draw.Count;
                    case SelectionMode.Perimeter: return _perimeter.Nodes.Count;
                    case SelectionMode.Block: return _blocks.Seeds.Count;
                    default: return _shape?.Ring.Length ?? 0;
                }
            }
        }
    }
}
