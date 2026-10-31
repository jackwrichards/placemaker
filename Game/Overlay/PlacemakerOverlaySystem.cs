// Placemaker — everything the tool draws on the map: outline and handles (vanilla district-tool
// look), snap feedback (PLT colour code), brush cursor, block hover, perimeter path, markers,
// the composed plan (nets by role colour and width, objects as one clear symbol per role, surfaces as a faint tint + outline and
// areas via FilledPreviewSystem), issues and build errors in red, and built groups in the
// Manage stage.
//
// Scales by construction: only the selection, the plan and bounded queries are drawn — never a
// walk over every node of the map each frame (Grid Road Generator's overlay did that).

using System;
using System.Collections.Generic;
using Colossal.Mathematics;
using Game;
using Game.Net;
using Game.Rendering;
using Game.Tools;
using Placemaker.Core;
using Placemaker.Core.Build;
using Placemaker.Core.Tool;
using Placemaker.Game.Selection;
using Placemaker.Game.Settings;
using Placemaker.Game.Tool;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Scripting;

namespace Placemaker.Game.Overlay
{
    public sealed partial class PlacemakerOverlaySystem : GameSystemBase
    {
        // Vanilla-like selection palette (district/area tools: light blue outline, white handles).
        private static readonly Color Outline = new Color(0.80f, 0.92f, 1f, 0.95f);
        private static readonly Color OutlineBad = new Color(1f, 0.35f, 0.3f, 0.95f);
        private static readonly Color Fill = new Color(0.30f, 0.62f, 1f, 0.10f);
        private static readonly Color FillHover = new Color(1f, 1f, 1f, 0.10f);
        private static readonly Color SitePlanFill = new Color(1f, 1f, 1f, 0f);   // the site plan draws its own ground (sp:base): no blue cast
        private static readonly Color FillUnderGhost = new Color(0.30f, 0.62f, 1f, 0.05f);   // barely tint the real surfaces
        private static readonly Color Handle = new Color(0.30f, 0.62f, 1f, 0.95f);
        private static readonly Color HandleHot = new Color(1f, 1f, 1f, 0.95f);
        /// <summary>(P2-1) Options: how solid the plan's ground fills draw (0.1..1), PlacemakerSettings.PreviewFillStrength.</summary>
        public static float FillStrength = 0.5f;
        private static readonly Color Ghost = new Color(1f, 1f, 1f, 0.55f);
        private static readonly Color Brush = new Color(0.30f, 0.62f, 1f, 0.35f);
        private static readonly Color Erase = new Color(1f, 0.45f, 0.35f, 0.35f);
        private static readonly Color Error = new Color(1f, 0.15f, 0.1f, 0.95f);
        private static readonly Color Warning = new Color(1f, 0.75f, 0.15f, 0.95f);
        private static readonly Color IssueOrange = new Color(1f, 0.6f, 0.12f, 0.95f);   // = PreviewLegend.Color("issues")
        private static readonly Color IssueNote = new Color(0.45f, 0.72f, 1f, 0.9f);
        private static readonly Color Manage = new Color(0.55f, 1f, 0.55f, 0.9f);
        private static readonly Color Marker = new Color(1f, 0.85f, 0.2f, 0.95f);
        private static readonly Color OutlineDim = new Color(0.80f, 0.92f, 1f, 0.35f);
        private static readonly Color ClippedFill = new Color(0.12f, 0.12f, 0.14f, 0.28f);

        private OverlayRenderSystem _overlay;
        private ToolSystem _toolSystem;
        private PlacemakerToolSystem _tool;
        private FilledPreviewSystem _fill;
        private AreaBorderRenderSystem _areaBorders;
        private BuildingLotRenderSystem _objectLots;
        private int _paintVersion = -1;
        private List<(float2 a, float2 b)> _paintBoundary = new List<(float2, float2)>();

        [Preserve]
        protected override void OnCreate()
        {
            base.OnCreate();
            _overlay = World.GetOrCreateSystemManaged<OverlayRenderSystem>();
            _toolSystem = World.GetOrCreateSystemManaged<ToolSystem>();
            _tool = World.GetOrCreateSystemManaged<PlacemakerToolSystem>();
            _fill = World.GetOrCreateSystemManaged<FilledPreviewSystem>();
            _areaBorders = World.GetOrCreateSystemManaged<AreaBorderRenderSystem>();
            _objectLots = World.GetOrCreateSystemManaged<BuildingLotRenderSystem>();
        }

        [Preserve]
        protected override void OnUpdate()
        {
            // the game's borders of planned areas (cyan lines, dashed rings round every piece) are off while our Real preview shows
            bool realPreview = _toolSystem.activeTool == _tool && _tool.PreviewModeEffective == PreviewMode.Ghost && _tool.Plan != null
                && (_tool.Stage == ToolStage.Design || _tool.Stage == ToolStage.Review);
            if (_areaBorders != null && _areaBorders.Enabled == realPreview) _areaBorders.Enabled = !realPreview;
            // and the dashed rings and lines round every planned tree, bush and lamp (the game's lots of temporary objects)
            if (_objectLots != null && _objectLots.Enabled == realPreview) _objectLots.Enabled = !realPreview;
            if (_toolSystem.activeTool != _tool)
            {
                if (_wasDrawing) { _fill.ClearAll(); _wasDrawing = false; }
                PolishedShown = 0;
                return;
            }
            if (!_tool.Painter) PolishedShown = 0;
            _wasDrawing = true;
            try
            {
                var buffer = _overlay.GetBuffer(out JobHandle deps);
                deps.Complete();
                _fill.BeginFrame();
                float w = LineWidth();
                var detail = ToolRegistration.Settings?.PreviewDetail ?? PreviewDetail.Normal;

                if (_tool.Stage == ToolStage.Manage) DrawManage(buffer, w);
                else
                {
                    // Global outgrow open: its lens or nothing (the lens off shows the plain map, even while brushing)
                    if (_tool.OutgrowLens) DrawOutgrowLens();
                    else if (_tool.Painter && !_tool.OutgrowCityOpen) DrawPolishedLots();
                    else PolishedShown = 0;
                    if ((_tool.Stage == ToolStage.Design || _tool.Stage == ToolStage.Review || _tool.Stage == ToolStage.Building) && !_tool.OutgrowMode)
                        DrawPlan(buffer, w, detail);
                    if (!_tool.OutgrowBrushOn && !_tool.OutgrowMode) DrawSelection(buffer, w);   // Outgrow: no area tool's cursor or hover
                    if (!_tool.OutgrowMode) { DrawMarkers(buffer, w); DrawTouches(buffer, w); DrawErrors(buffer, w); }
                }
                if (_tool.OutgrowBrushOn) DrawOutgrowBrush(buffer, w);
                _fill.EndFrame();
            }
            catch (Exception e)
            {
                if (_errors++ < 10) Mod.Log.Error(e, "Placemaker: overlay failed");
            }
        }

        private bool _wasDrawing;
        private int _errors;

        /// <summary>Line width that stays visible from far away without being fat up close.</summary>
        private float LineWidth()
        {
            var cam = Camera.main;
            if (cam == null) return 1f;
            float3 camPos = cam.transform.position;
            // Distance to what is being edited (the outline), else height above it — not the absolute
            // altitude, so mountain cities do not get fat lines.
            float h = camPos.y;
            var shape = _tool.Shape;
            if (shape != null && shape.Ring.Length > 0)
            {
                float3 c = float3.zero;
                foreach (var p in shape.Ring) c += p;
                c /= shape.Ring.Length;
                h = math.distance(camPos, c);
            }
            return math.clamp(math.max(10f, h) * 0.0022f, 0.25f, 6f);
        }

        // ───────────── selection ─────────────

        private static readonly Color KeptBuilding = new Color(1f, 0.3f, 0.25f, 0.22f);
        private int _keptSigVersion = -1;
        private long _keptSig;
        private int _paintNearVersion = -1;
        private float2 _paintNearAt = new float2(float.MaxValue);
        private List<float3[]> _paintNear = new List<float3[]>();
        private long _paintNearSig;

        /// <summary>
        /// Plain Paint (keep off buildings on): the buildings near the brush, shaded while painting. Re-filtered
        /// only after the cursor moved 16 m or the gathered set changed, so the fill mesh is not rebuilt per frame.
        /// </summary>
        private void DrawPaintBuildings()
        {
            if (!_tool.PaintAvoidsBuildings || !_tool.CursorValid) return;
            var all = _tool.PaintBuildings3D;
            if (all.Count == 0) return;
            float2 at = _tool.Cursor.xz;
            if (_paintNearVersion != _tool.PaintBuildingsVersion || math.distance(at, _paintNearAt) >= 16f)
            {
                _paintNearVersion = _tool.PaintBuildingsVersion;
                _paintNearAt = at;
                float reach = _tool.BrushSize * 0.5f + 60f;
                var list = new List<float3[]>();
                foreach (var o in all)
                {
                    float2 mn = new float2(float.MaxValue), mx = new float2(float.MinValue);
                    foreach (var p in o) { mn = math.min(mn, p.xz); mx = math.max(mx, p.xz); }
                    if (math.length(math.max(math.max(mn - at, at - mx), 0f)) <= reach) list.Add(o);
                }
                _paintNear = list;
                _paintNearSig = FilledPreviewSystem.Signature(list);
            }
            if (_paintNear.Count > 0) _fill.SetLayer("paintBuildings", _paintNear, KeptBuilding, -93.8f, _paintNearSig);
        }

        private int _buildsSigVersion = -1;
        private long _buildsSig;

        private void DrawSelection(OverlayRenderSystem.Buffer buffer, float w)
        {
            var shape = _tool.Shape;
            bool bad = _tool.AreaProblem() != null;
            // s15: during a brush stroke the outline is stale until mouse-up — only the painted cells are drawn.
            bool stroke = _tool.Painting && (_tool.Mode == SelectionMode.Paint || _tool.Mode == SelectionMode.BlockPaint);
            if (shape != null && shape.Ring.Length >= 3 && !stroke)
            {
                var rings = new[] { shape.Ring };
                _fill.SetLayer("selection", rings, SitePlanStyle && _tool.Stage != ToolStage.Area && _tool.Plan != null ? SitePlanFill : _tool.SketchObjects == SketchObjects.Hidden ? FillUnderGhost : Fill, -94f, FilledPreviewSystem.Signature(rings));
            }

            // Keep off existing roads: the parts on roads are left out — shaded grey, the outline used is drawn solid.
            var away = _tool.ClippedAway;
            bool clipped = away != null && away.Count > 0 && shape != null && !stroke;
            bool cutBack = false;
            // s27: other Placemaker builds (and, with "clip to free land", the other stops) the outline was cut against.
            var builds = _tool.ClippedObstacles;
            if (builds.Count > 0 && !stroke && _tool.Stage != ToolStage.Building)
            {
                if (_buildsSigVersion != _tool.ClippedObstaclesVersion)
                {
                    _buildsSigVersion = _tool.ClippedObstaclesVersion;
                    _buildsSig = FilledPreviewSystem.Signature(builds);
                }
                _fill.SetLayer("clippedBuilds", builds, KeptBuilding, -93.75f, _buildsSig);
                cutBack = shape != null;
            }
            if (clipped)
            {
                var list = new List<float3[]>(away);
                _fill.SetLayer("clipped", list, ClippedFill, -93.9f, FilledPreviewSystem.Signature(list));
            }

            // s15: existing buildings under the selection stay — shaded red, in every mode.
            var kept = _tool.BuildingsInSelection;
            if (kept.Count > 0 && shape != null && !stroke && _tool.Stage != ToolStage.Building)
            {
                if (_keptSigVersion != _tool.BuildingsInSelectionVersion)
                {
                    _keptSigVersion = _tool.BuildingsInSelectionVersion;
                    _keptSig = FilledPreviewSystem.Signature(kept);
                }
                _fill.SetLayer("keptBuildings", kept, KeptBuilding, -93.7f, _keptSig);
            }

            switch (_tool.Mode)
            {
                case SelectionMode.Draw: DrawDrawMode(buffer, w, bad, clipped || cutBack); break;
                case SelectionMode.Block: DrawBlockMode(buffer, w, shape); break;
                case SelectionMode.Perimeter: DrawPerimeterMode(buffer, w, shape); break;
                case SelectionMode.Paint: DrawPaintMode(buffer, w, shape); break;
                case SelectionMode.BlockPaint: DrawBlockPaintMode(buffer, w, shape); break;
            }
            if ((clipped || cutBack) && shape != null && _tool.Mode == SelectionMode.Draw) Ring(buffer, shape.Ring, Outline, w);
            // the Real preview: what will be built and a thin line on the ground round it (the user, 25 Oct: "I just want to see what it
            // would really look like"; the thick outline stood up as a glass wall), the game's borders of every piece hidden meanwhile
            if (_tool.PreviewModeEffective == PreviewMode.Ghost && shape != null && shape.Ring.Length >= 3 && !stroke)
            {
                var line = new Color(Outline.r, Outline.g, Outline.b, 0.7f);
                for (int i = 0; i < shape.Ring.Length; i++)
                    buffer.DrawLine(line, line, 0f, OverlayRenderSystem.StyleFlags.Projected, new Line3.Segment(shape.Ring[i], shape.Ring[(i + 1) % shape.Ring.Length]), math.max(0.5f, w * 0.8f), new float2(1, 1));
            }
        }

        private void Ring(OverlayRenderSystem.Buffer buffer, float3[] ring, Color c, float w, bool dashed = false)
        {
            for (int i = 0; i < ring.Length; i++)
            {
                var seg = new Line3.Segment(ring[i], ring[(i + 1) % ring.Length]);
                if (dashed) buffer.DrawDashedLine(c, seg, w, w * 4f, w * 3f);
                else buffer.DrawLine(c, seg, w);
            }
        }

        private void DrawDrawMode(OverlayRenderSystem.Buffer buffer, float w, bool bad, bool clipped)
        {
            var draw = _tool.Draw;
            var pts = draw.Points;
            int n = pts.Count;
            if (n == 0)
            {
                if (_tool.CursorValid) buffer.DrawCircle(Ghost, _tool.Cursor, w * 6f);
                DrawSnap(buffer, w);
                return;
            }
            var line = bad ? OutlineBad : clipped ? OutlineDim : Outline;
            for (int i = 0; i + 1 < n; i++) DrawEdge(buffer, w, i, pts[i], pts[i + 1], line);
            if (draw.Closed) DrawEdge(buffer, w, n - 1, pts[n - 1], pts[0], line);
            else if (_tool.CursorValid)
            {
                // Rubber band to the cursor and the closing edge preview.
                buffer.DrawDashedLine(Ghost, new Line3.Segment(pts[n - 1], _tool.Cursor), w, w * 4f, w * 3f);
                if (n >= 2) buffer.DrawDashedLine(new Color(1f, 1f, 1f, 0.25f), new Line3.Segment(_tool.Cursor, pts[0]), w * 0.7f, w * 4f, w * 3f);
            }
            float d = w * 5f;
            for (int i = 0; i < n; i++)
            {
                bool hot = i == _tool.HoverPoint || (i == 0 && _tool.HoverClose);
                buffer.DrawCircle(hot ? HandleHot : Outline, hot ? HandleHot : Handle, w * 0.6f, 0, new float2(0, 1), pts[i], hot ? d * 1.4f : d);
            }
            if (_tool.EdgeDragging) buffer.DrawCircle(HandleHot, _tool.EdgeGuidePoint, d);
            DrawSnap(buffer, w);
        }

        /// <summary>One edge of the drawn outline: along its road when both corners sit on road nodes (area overhaul), else straight.</summary>
        private void DrawEdge(OverlayRenderSystem.Buffer buffer, float w, int i, float3 a, float3 b, Color line)
        {
            var c = _tool.HoverEdge == i ? HandleHot : line;
            float width = _tool.HoverEdge == i ? w * 1.6f : w;
            var route = _tool.RouteOfEdge(i);
            if (route == null) { buffer.DrawLine(c, new Line3.Segment(a, b), width); return; }
            for (int k = 0; k + 1 < route.Length; k++) buffer.DrawLine(c, new Line3.Segment(route[k], route[k + 1]), width);
        }

        private void DrawSnap(OverlayRenderSystem.Buffer buffer, float w)
        {
            var s = _tool.LastSnap;
            if (s.Kind == SnapKind.None) return;
            var c = SnapColor(s.Kind);
            if (s.HasGuide) buffer.DrawDashedLine(new Color(c.r, c.g, c.b, 0.6f), new Line3.Segment(s.GuideA, s.GuideB), w * 0.6f, w * 3f, w * 3f);
            buffer.DrawCircle(c, new Color(0, 0, 0, 0), w * 0.8f, 0, new float2(0, 1), s.Position, w * 9f);
        }

        // PLT's colour code: curb orange, building red, neighbour area green, axis violet, guide amber, zone grid teal, two at once white.
        private static Color SnapColor(SnapKind k)
        {
            switch (k)
            {
                case SnapKind.RoadEdge: return new Color(1f, 0.55f, 0.1f, 1f);
                case SnapKind.ObjectSide: return new Color(1f, 0.25f, 0.25f, 1f);
                case SnapKind.AreaEdge: return new Color(0.35f, 0.95f, 0.4f, 1f);
                case SnapKind.Direction: return new Color(0.75f, 0.45f, 1f, 1f);
                case SnapKind.Guide: return new Color(1f, 0.78f, 0.25f, 1f);
                case SnapKind.ZoneGrid: return new Color(0.25f, 0.9f, 0.85f, 1f);
                case SnapKind.RoadNode: return new Color(1f, 1f, 1f, 1f);
                default: return Color.white;
            }
        }

        // Area overhaul: what the fill shows while hovering — closed gaps (amber, thick dashes), the reach (pale blue dashes)
        // where the fill met it, and the stops along its edge (red tint). Legend rows: AreaLegend (area:*).
        private static readonly Color DoorColor = new Color(1f, 0.8f, 0.2f, 0.95f);
        private static readonly Color ReachColor = new Color(0.55f, 0.85f, 1f, 0.7f);

        private PlacemakerToolSystem.BlockPick _stopsFor;
        private long _stopsSig;

        private void DrawFillExtras(OverlayRenderSystem.Buffer buffer, float w, PlacemakerToolSystem.BlockPick pick)
        {
            if (pick == null) return;
            if (_tool.LayerVisible(AreaLegend.Stops) && pick.Stops != null && pick.Stops.Count > 0)
            {
                if (!ReferenceEquals(_stopsFor, pick)) { _stopsFor = pick; _stopsSig = FilledPreviewSystem.Signature(pick.Stops); }
                _fill.SetLayer("fillStops", pick.Stops, Blocked, -93.8f, _stopsSig);
            }
            if (_tool.LayerVisible(AreaLegend.Doors) && pick.Doors != null)
                foreach (var d in pick.Doors)
                    for (int i = 0; i + 1 < d.Length; i++) buffer.DrawDashedLine(DoorColor, new Line3.Segment(d[i], d[i + 1]), w * 1.4f, w * 3f, w * 2f);
            if (_tool.LayerVisible(AreaLegend.Reach) && pick.Reach != null)
                for (int i = 0; i < pick.Reach.Length; i++)
                    buffer.DrawDashedLine(ReachColor, new Line3.Segment(pick.Reach[i], pick.Reach[(i + 1) % pick.Reach.Length]), w * 0.8f, w * 5f, w * 4f);
        }

        private void DrawBlockMode(OverlayRenderSystem.Buffer buffer, float w, AreaShape shape)
        {
            if (shape != null && _tool.LayerVisible(AreaLegend.Outline)) Ring(buffer, shape.Ring, Outline, w);
            var hover = _tool.HoverFace;
            if (hover?.Area != null && _tool.Stage == ToolStage.Area)
            {
                if (_tool.LayerVisible(AreaLegend.Fill))
                {
                    var rings = new[] { hover.Area.Ring };
                    _fill.SetLayer("hover", rings, FillHover, -93.5f, FilledPreviewSystem.Signature(rings));
                    Ring(buffer, hover.Area.Ring, HandleHot, w * 0.8f, dashed: true);
                }
                DrawFillExtras(buffer, w, hover);
            }
            if (_tool.BucketReachLive) DrawBucketReach(buffer);
        }

        // The bucket's reach at the cursor: one dashed circle, bright with a dark rim, lying on the ground (projected). Its dashes
        // are a fixed share of the circle and each is a fixed width on screen, so the far side reads as well as the near one.
        private static readonly Color ReachRim = new Color(0.02f, 0.06f, 0.12f, 0.9f);
        private static readonly Color ReachCursor = new Color(0.6f, 0.92f, 1f, 1f);
        private const int ReachDashes = 72;
        private const float ReachPixels = 12f;

        private void DrawBucketReach(OverlayRenderSystem.Buffer buffer)
        {
            var cam = Camera.main;
            if (cam == null) return;
            float3 eye = cam.transform.position, c = _tool.Cursor;
            float r = _tool.BucketReach;
            float perPx = 2f * math.tan(math.radians(cam.fieldOfView) * 0.5f) / math.max(Screen.height, 1);   // m per pixel, 1 m away
            float step = 2f * math.PI / ReachDashes;
            for (int i = 0; i < ReachDashes; i++)
            {
                float a = i * step, b = a + step * 0.55f;
                var p = new float3(c.x + r * math.cos(a), c.y, c.z + r * math.sin(a));
                var q = new float3(c.x + r * math.cos(b), c.y, c.z + r * math.sin(b));
                float width = math.max(math.distance(eye, (p + q) * 0.5f) * perPx * ReachPixels, 0.3f);
                buffer.DrawLine(ReachRim, ReachCursor, width * 0.25f, OverlayRenderSystem.StyleFlags.Projected, new Line3.Segment(p, q), width, new float2(1f, 1f));
            }
        }

        private void DrawPerimeterMode(OverlayRenderSystem.Buffer buffer, float w, AreaShape shape)
        {
            var per = _tool.Perimeter;
            var em = EntityManager;
            // s43 (playtest: "should be cleaner"): thinner picked roads, small node dots, the closing dashed.
            var em2 = em;
            foreach (var h in per.Path)
            {
                if (RoadGraphAlgorithms.IsStraight(h))
                {
                    buffer.DrawLine(Handle, new Line3.Segment(_tool.NodePositionOf(h.From), _tool.NodePositionOf(h.To)), w * 1.1f);
                    continue;
                }
                var e = EntityIds.Resolve(em2, h.Edge);
                if (e == Entity.Null || !em2.HasComponent<Curve>(e)) continue;
                buffer.DrawCurve(Handle, em2.GetComponentData<Curve>(e).m_Bezier, w * 1.1f);
            }
            var closing = _tool.PerimeterClosing;
            if (closing != null)
            {
                var dash = new Color(1f, 1f, 1f, 0.7f);
                foreach (var h in closing)
                {
                    var e = RoadGraphAlgorithms.IsStraight(h) ? Entity.Null : EntityIds.Resolve(em2, h.Edge);
                    if (e != Entity.Null && em2.HasComponent<Curve>(e))
                    {
                        // Dashed along the road: its curve as short chords.
                        var c = em2.GetComponentData<Curve>(e).m_Bezier;
                        for (int k = 0; k < 8; k++)
                            buffer.DrawDashedLine(dash, new Line3.Segment(MathUtils.Position(c, k / 8f), MathUtils.Position(c, (k + 1) / 8f)), w * 0.8f, w * 4f, w * 3f);
                    }
                    else buffer.DrawDashedLine(dash, new Line3.Segment(_tool.NodePositionOf(h.From), _tool.NodePositionOf(h.To)), w * 0.8f, w * 4f, w * 3f);
                }
            }
            float d = w * 3.2f;
            for (int i = 0; i < per.Positions.Count; i++)
                buffer.DrawCircle(Outline, i == 0 ? HandleHot : Handle, w * 0.4f, 0, new float2(0, 1), per.Positions[i], i == 0 ? d * 1.4f : d);
            if (shape != null) Ring(buffer, shape.Ring, OutlineDim, w * 0.6f);
            var node = _tool.HoverNode;
            if (node != Entity.Null && em.Exists(node) && em.HasComponent<Node>(node))
                buffer.DrawCircle(HandleHot, new Color(1, 1, 1, 0.3f), w * 0.6f, 0, new float2(0, 1), em.GetComponentData<Node>(node).m_Position, d * 1.6f);
        }

        private void DrawPaintMode(OverlayRenderSystem.Buffer buffer, float w, AreaShape shape)
        {
            var paint = _tool.Paint;
            if (_tool.Painting) DrawPaintBuildings();
            if (paint.Version != _paintVersion)
            {
                _paintVersion = paint.Version;
                _paintBoundary = paint.RawBoundary();
            }
            // Raw cell boundary while painting; the smoothed outline once the shape exists.
            if (_tool.Painting || shape == null)
            {
                int max = math.min(_paintBoundary.Count, 20000);
                for (int i = 0; i < max; i++)
                {
                    var (a, b) = _paintBoundary[i];
                    buffer.DrawLine(Outline, new Line3.Segment(OnGround(a), OnGround(b)), w * 0.7f);
                }
            }
            else Ring(buffer, shape.Ring, Outline, w);
            if (_tool.BrushLive) DrawBrushCursor(buffer, w, _tool.Painting ? Brush : new Color(Brush.r, Brush.g, Brush.b, 0.2f));
        }

        private void DrawBrushCursor(OverlayRenderSystem.Buffer buffer, float w, Color c)
        {
            if (!_tool.CursorValid) return;
            float size = _tool.BrushSize;
            if (!_tool.BrushSquare)
                buffer.DrawCircle(Outline, c, w * 0.6f, 0, new float2(0, 1), _tool.Cursor, size);
            else
            {
                float a = math.radians(_tool.BrushAngle);
                float2 x = new float2(math.cos(a), math.sin(a)) * size * 0.5f, z = new float2(-x.y, x.x);
                var p = _tool.Cursor;
                var corners = new[]
                {
                    new float3(p.x - x.x - z.x, p.y, p.z - x.y - z.y), new float3(p.x + x.x - z.x, p.y, p.z + x.y - z.y),
                    new float3(p.x + x.x + z.x, p.y, p.z + x.y + z.y), new float3(p.x - x.x + z.x, p.y, p.z - x.y + z.y),
                };
                Ring(buffer, corners, Outline, w * 0.8f);
            }
        }

        // ───────────── markers ─────────────

        private void DrawMarkers(OverlayRenderSystem.Buffer buffer, float w)
        {
            var recipe = _tool.CurrentRecipe;
            if (recipe == null || _tool.Shape == null) return;
            // s22: entrances are a legend layer too (always shown while placing them).
            if (_tool.MarkerKind == null && !Shows(PreviewLegend.Entrances)) return;
            float d = w * 7f;
            // Each kind has its own colour (MarkerKinds.ColorFor: two-way yellow, entry green, exit orange…);
            // markers of the step being edited are drawn larger with a white rim.
            foreach (var kv in RecipePaths.Walk(recipe))
            {
                bool stepActive = kv.Key == _tool.MarkerPath;
                foreach (var m in kv.Value.Markers)
                {
                    var c = KindColor(m.Kind, 0.95f);
                    float size = stepActive ? d * 1.3f : d;
                    buffer.DrawCircle(stepActive ? HandleHot : c, c, w * 0.6f, 0, new float2(0, 1), m.Position, size);
                    // Inner dot: entry/exit read at a glance even for colour-blind players (entry filled, exit hollow).
                    if (m.Kind == "exit" || m.Kind == "alleyOut") buffer.DrawCircle(Color.white, new Color(0, 0, 0, 0.6f), w * 0.4f, 0, new float2(0, 1), m.Position, size * 0.45f);
                    else if (m.Kind == "entry" || m.Kind == "alleyIn") buffer.DrawCircle(Color.white, Color.white, w * 0.4f, 0, new float2(0, 1), m.Position, size * 0.35f);
                    // An entrance with a crosswalk: a white ring round it, like the road markings it brings.
                    if (MarkerVariants.HasCrosswalk(m.Kind)) buffer.DrawCircle(Color.white, new Color(0, 0, 0, 0), w * 0.5f, 0, new float2(0, 1), m.Position, size * 1.5f);
                }
            }
            if (_tool.MarkerKind != null && _tool.MarkerHoverValid)
            {
                // (P2-1) red when too close to another entrance (PLT's spacing rule)
                buffer.DrawCircle(_tool.MarkerBlocked ? new Color(1f, 0.25f, 0.2f, 0.95f) : HandleHot, _tool.MarkerBlocked ? new Color(1f, 0.25f, 0.2f, 0.4f) : KindColor(_tool.MarkerKind, 0.4f), w * 0.6f, 0, new float2(0, 1), _tool.MarkerHover, d * 1.3f);
                // (P2-1) snapped to a drive aisle: a dashed guide down the aisle it will join
                if (_tool.MarkerSnapped)
                    buffer.DrawDashedLine(KindColor(_tool.MarkerKind, 0.8f), new Line3.Segment(_tool.MarkerHover, _tool.MarkerSnapTo), w * 0.5f, w * 3f, w * 2f);
            }
            // (P2-1) the placed marker under the cursor can be dragged: a white ring round it
            if (_tool.MarkerKind != null && _tool.MarkerHot >= 0)
            {
                var st = RecipePaths.Resolve(recipe, _tool.MarkerPath);
                if (st != null && _tool.MarkerHot < st.Markers.Count)
                    buffer.DrawCircle(HandleHot, new Color(0, 0, 0, 0), w * 0.6f, 0, new float2(0, 1), st.Markers[_tool.MarkerHot].Position, d * 1.9f);
            }
        }

        // ───────────── hand edits (P2-1, Core/Touches.cs) ─────────────

        /// <summary>The colour of a touch target's state: what the stall is now.</summary>
        private static Color TouchColor(string state, float a)
        {
            switch (state)
            {
                case "accessible": return new Color(0.25f, 0.55f, 1f, a);
                case "ev": return new Color(0.3f, 0.9f, 0.45f, a);
                case "island": return new Color(0.35f, 0.75f, 0.25f, a);
                case "blank": return new Color(0.6f, 0.6f, 0.6f, a);
                case "picked": return new Color(1f, 0.82f, 0.2f, a);
                default: return new Color(1f, 1f, 1f, a);
            }
        }

        /// <summary>
        /// While a hand-edit kind is picked: every target of its group in a thin outline (hand-edited ones brighter, in their
        /// state's colour) and the one under the cursor filled, so the player sees what a click will change.
        /// </summary>
        private void DrawTouches(OverlayRenderSystem.Buffer buffer, float w)
        {
            var kind = _tool.TouchKind;
            var plan = _tool.Plan;
            if (kind == null || plan == null) return;
            string group = Touches.GroupOf(kind);
            var hover = _tool.TouchHover;
            foreach (var layer in plan.Layers)
            {
                if (layer?.TouchTargets == null || Touches.StepOf(layer.StepPath) != _tool.TouchPath) continue;
                foreach (var t in layer.TouchTargets)
                {
                    if (t == null || t.Group != group || t.Outline == null || t.Outline.Length < 2) continue;
                    bool hot = ReferenceEquals(t, hover);
                    bool touched = !string.IsNullOrEmpty(t.Touched);
                    var c = TouchColor(t.State, hot ? 1f : touched ? 0.85f : 0.35f);
                    float width = math.max(0.15f, w * (hot ? 0.9f : touched ? 0.6f : 0.3f));
                    for (int i = 0; i < t.Outline.Length; i++)
                    {
                        var a = t.Outline[i]; var b = t.Outline[(i + 1) % t.Outline.Length];
                        buffer.DrawLine(c, c, 0f, OverlayRenderSystem.StyleFlags.Projected,
                            new Line3.Segment(new float3(a.x, t.Center.y, a.y), new float3(b.x, t.Center.y, b.y)), width, new float2(1, 1));
                    }
                    if (hot) buffer.DrawCircle(HandleHot, TouchColor(t.State, 0.35f), w * 0.5f, 0, new float2(0, 1), t.Center, w * 6f);
                }
            }
        }

        // ───────────── plan ─────────────
        //
        // s22 visual hierarchy (playtest: "it's getting crazy; hard to even see what's happening"): the outline,
        // paths / roads and the key features read first; trees, planting and furniture are small and soft; surfaces
        // a faint tint; terraform a heat map (no dashed boxes); cutouts a calm blue fill with a thin outline; cells
        // blocked for zoning and decals off unless the legend shows them. Every element belongs to one legend layer
        // (Core Build/PreviewLegend) and is drawn only while that layer is visible (legend hover isolates one).

        private ComposedPlan _filledPlan;
        private readonly Dictionary<int, List<float3[]>> _surfaceGroups = new Dictionary<int, List<float3[]>>();
        private readonly Dictionary<int, List<float3[]>> _areaGroups = new Dictionary<int, List<float3[]>>();
        /// <summary>(s13) Texture paint preview: coarse strength bands per (material layer, level), from Core PaintRaster.</summary>
        private readonly Dictionary<int, List<float3[]>> _paintGroups = new Dictionary<int, List<float3[]>>();
        private const int PaintLevels = 4;
        /// <summary>(s22) Terraform heat map: rings per band (−4…−1 cut, 1…4 fill), from Core TerrainHeat.</summary>
        private readonly Dictionary<int, List<float3[]>> _heatGroups = new Dictionary<int, List<float3[]>>();

        // (sketch study) the site plan never isolates a layer on hover (the hovered row blinks instead: SpPulse)
        private bool Shows(string layer) => SitePlanStyle && _tool.Plan != null ? SpShows(layer) : _tool.LayerVisible(layer);

        private void DrawPlan(OverlayRenderSystem.Buffer buffer, float w, PreviewDetail detail)
        {
            var plan = _tool.Plan;
            if (plan == null) return;
            bool stale = _tool.PlanStale;
            float alpha = stale ? 0.45f : 1f;
            // Ghost on the map (real models/surfaces): the sketch steps back — hidden when the ghost shows this plan,
            // dimmed while it catches up. Fills keep their meshes (alpha 0) so switching back is instant.
            var objects = _tool.SketchObjects;
            if (objects == SketchObjects.Dimmed) alpha *= 0.3f;
            bool content = objects != SketchObjects.Hidden;
            float fillAlpha = content ? alpha : 0f;
            bool isolating = !string.IsNullOrEmpty(_tool.LegendHover);

            if (!ReferenceEquals(plan, _filledPlan))
            {
                _filledPlan = plan;
                _categoryInfo.Clear();
                try { _assetInfo = Services.Assets?.InfoMap(); } catch (Exception) { _assetInfo = null; }
                _surfaceGroups.Clear();
                _areaGroups.Clear();
                BuildPaintPreview(plan);
                BuildHeatPreview(plan);
                BuildZoneCellPreview(plan);
                foreach (var l in plan.Layers)
                {
                    if (l == null) continue;
                    foreach (var s in l.Surfaces)
                        if (s?.Ring != null && s.Ring.Length >= 3) Group(_surfaceGroups, (int)s.Role).Add(s.Ring);
                    foreach (var a in l.Areas)
                        if (a?.Ring != null && a.Ring.Length >= 3) Group(_areaGroups, (int)a.Role).Add(a.Ring);
                }
            }
            // (sketch study) Sketch style "Site plan": the plan as what gets built (PlacemakerOverlaySystem.SitePlan.cs); the terrain
            // heat map (a little quieter) and the crosswalks stay as they are.
            if (SitePlanStyle)
            {
                DrawSitePlan(buffer, w, alpha, objects);
                // the ground change (red raised, blue lowered): hidden until its legend row (Terrain) turns it on, then on the map and in
                // the panel's top view alike (the user: the world always matches the top view)
                bool showHeat = Shows(PreviewLegend.Terrain);
                foreach (var kv in _heatGroups)
                {
                    var (hex, a) = TerrainHeat.Style(kv.Key);
                    _fill.SetLayer("heat:" + kv.Key, kv.Value, WithAlpha(Hex(hex), showHeat ? a * (stale ? 0.5f : 1f) : 0f), -92.8f + kv.Key * 0.001f, FilledPreviewSystem.Signature(kv.Value));
                }
                if (Shows(PreviewLegend.Entrances) || Shows(PreviewLegend.Paths)) DrawCrosswalks(buffer, plan, w, stale ? 0.45f : 1f);
                return;
            }
            // FEEDBACK-4 #4: surfaces are a faint tint (≤ 15 %) — or outline only — with a thin outline in the role's
            // colour (dashed for paved roles), so paths and objects on top stay readable.
            bool tint = ToolRegistration.Settings?.SketchSurfaceFill ?? true;
            bool surfaces = Shows(PreviewLegend.Surfaces), water = Shows(PreviewLegend.Water);
            foreach (var kv in _surfaceGroups)
            {
                var role = (SurfaceRole)kv.Key;
                bool on = role == SurfaceRole.Water ? water : surfaces;
                // Isolated: a little stronger, so a faint layer is findable on its own.
                float boost = isolating && on ? 1.8f : 1f;
                _fill.SetLayer("surface:" + kv.Key, kv.Value, WithAlpha(SurfaceColor(role), on ? (tint || isolating ? SurfaceTint(role) * boost : 0f) * fillAlpha : 0f), -93f + kv.Key * 0.01f, FilledPreviewSystem.Signature(kv.Value));
                if (!content || !on) continue;
                bool paved = role == SurfaceRole.Paving || role == SurfaceRole.Plaza || role == SurfaceRole.Asphalt;
                var edge = WithAlpha(SurfaceColor(role), (role == SurfaceRole.Water ? 0.75f : 0.5f) * alpha);
                foreach (var r in kv.Value) Ring(buffer, r, edge, w * (role == SurfaceRole.Water ? 0.6f : 0.35f), dashed: paved);
            }
            // Texture paint: tinted strength bands (feather and gradients show as steps), no outline: it is a blend.
            foreach (var kv in _paintGroups)
            {
                int layer = kv.Key / 16, level = kv.Key % 16;
                _fill.SetLayer("paint:" + kv.Key, kv.Value, WithAlpha(PaintColor(layer), surfaces ? 0.26f * level / PaintLevels * fillAlpha : 0f), -93.2f, FilledPreviewSystem.Signature(kv.Value));
            }
            foreach (var kv in _areaGroups)
            {
                _fill.SetLayer("area:" + kv.Key, kv.Value, WithAlpha(AreaColor((AreaRole)kv.Key), surfaces && tint ? 0.05f * fillAlpha : 0f), -93.5f, FilledPreviewSystem.Signature(kv.Value));
                if (content && surfaces)
                    foreach (var r in kv.Value) Ring(buffer, r, WithAlpha(AreaColor((AreaRole)kv.Key), 0.3f * alpha), w * 0.3f, dashed: true);
            }
            // Terraform heat map (cut blue, fill orange → red, stronger = bigger change). Shown under the ghost too:
            // the ghost does not show the ground change before the build.
            bool terrain = Shows(PreviewLegend.Terrain);
            float heatAlpha = stale ? 0.5f : 1f;
            foreach (var kv in _heatGroups)
            {
                var (hex, a) = TerrainHeat.Style(kv.Key);
                _fill.SetLayer("heat:" + kv.Key, kv.Value, WithAlpha(Hex(hex), terrain ? a * heatAlpha * (isolating ? 1.5f : 1f) : 0f), -92.8f + kv.Key * 0.001f, FilledPreviewSystem.Signature(kv.Value));
            }
            DrawZoneCells(buffer, w, alpha, isolating);

            // Objects (playtest 4b #9): every feature object always gets a symbol; only ground clutter (decals,
            // bushes, planters) is thinned to the detail budget (Simple hides clutter only).
            int netBudget = detail == PreviewDetail.Detailed ? 20000 : 6000;
            int clutterBudget = detail == PreviewDetail.Simple ? 0 : detail == PreviewDetail.Detailed ? 30000 : 8000;
            int mainBudget = detail == PreviewDetail.Detailed ? 30000 : 12000;
            // Isolating clutter (planting, decals) shows all of it: that is what the player asked to see.
            if (isolating) clutterBudget = 30000;
            int clutterCount = 0;
            foreach (var l in plan.Layers)
                if (l != null)
                    foreach (var o in l.Objects) if (o != null && IsClutter(o) && Shows(PreviewLegend.Of(o))) clutterCount++;
            int stride = clutterBudget <= 0 ? int.MaxValue : math.max(1, (int)math.ceil(clutterCount / (float)clutterBudget));
            int k = 0;
            foreach (var l in plan.Layers)
            {
                if (l == null) continue;
                foreach (var n in l.Nets)
                {
                    if (n == null || !content) break;
                    if (!Shows(PreviewLegend.Of(n, l.GeneratorId))) continue;
                    if (netBudget-- <= 0) break;
                    DrawNet(buffer, n, w, alpha);
                }
            }
            // Entrances with a crosswalk: white zebra stripes across the street where the path will split it, and the path
            // carried on to the street's centre line, so the plan says which entrances cut the street (shown with the ghost too:
            // it only shows the crossing once the street is split).
            if (Shows(PreviewLegend.Entrances) || Shows(PreviewLegend.Paths)) DrawCrosswalks(buffer, plan, w, stale ? 0.45f : 1f);
            // Clutter first, then features; trees last so crowns read as a canopy over the furniture.
            for (int pass = 0; pass < 3 && content; pass++)
                foreach (var l in plan.Layers)
                {
                    if (l == null) continue;
                    foreach (var o in l.Objects)
                    {
                        if (o == null) continue;
                        bool clutter = IsClutter(o);
                        int p = clutter ? 0 : o.Role == ObjectRole.Tree ? 2 : 1;
                        if (p != pass) continue;
                        if (!Shows(PreviewLegend.Of(o))) continue;
                        if (clutter ? (k++ % stride) != 0 : mainBudget-- <= 0) continue;
                        DrawObject(buffer, o, w, alpha);
                    }
                }
            // Zoning quads the guard could not snap to captured cells (lanes this build adds): thin outline.
            if (Shows(PreviewLegend.Cutouts))
                foreach (var l in plan.Layers)
                {
                    if (l == null) continue;
                    foreach (var z in l.Zoning)
                        if (z != null && !_snappedZoning) Ring(buffer, new[] { z.A, z.B, z.C, z.D }, WithAlpha(ZoneCellColor(ZoneColorKey(z.ZoneId)), 0.7f * alpha), w * 0.35f);
                }
            // (s61) The ring markers are the "issues" legend layer: red = left out / blocked, orange = a warning, blue = a note.
            // Hover the legend row to see only them; every marker has an entry in the issue chips (reason + the setting).
            if (Shows(PreviewLegend.Issues))
                foreach (var issue in plan.AllIssues())
                {
                    if (issue?.At == null) continue;
                    bool red = issue.Dropped || issue.Severity == IssueSeverity.Error;
                    var col = red ? Error : issue.Severity == IssueSeverity.Warning ? IssueOrange : IssueNote;
                    buffer.DrawCircle(col, new Color(col.r, col.g * 0.4f, 0f, 0.15f), w, 0, new float2(0, 1), issue.At.Value, w * 14f);
                }
        }

        private static Color Hex(string hex)
        {
            var c = PreviewLegend.Hex(hex);
            return new Color(c.x, c.y, c.z, 1f);
        }

        private static Color LayerColor(string layer)
        {
            var c = PreviewLegend.Rgb(layer);
            return new Color(c.x, c.y, c.z, 1f);
        }

        private void BuildHeatPreview(ComposedPlan plan)
        {
            _heatGroups.Clear();
            var heat = plan.TerrainHeat;
            if (heat == null) return;
            foreach (var (band, ring) in heat.Rings)
            {
                if (ring == null || ring.Length < 3) continue;
                var r3 = new float3[ring.Length];
                for (int i = 0; i < ring.Length; i++) r3[i] = new float3(ring[i].x, 0f, ring[i].y);
                Group(_heatGroups, band).Add(r3);
            }
        }

        // ───────────── zone cells (s19, calmer in s22) ─────────────

        /// <summary>Zoned cells per zone colour (0 commercial, 1 residential, 2 office, 3 industrial), 8 blocked, 9 kept.</summary>
        private readonly Dictionary<int, List<float3[]>> _zoneCellGroups = new Dictionary<int, List<float3[]>>();
        private readonly List<float3[]> _zoneBlockerRings = new List<float3[]>();
        private const int BlockedCells = 8, KeptCells = 9;
        /// <summary>The plan's zoning was snapped to captured cells (the cells are drawn, not the quads).</summary>
        private bool _snappedZoning;

        private void BuildZoneCellPreview(ComposedPlan plan)
        {
            _zoneCellGroups.Clear();
            _zoneBlockerRings.Clear();
            _snappedZoning = plan.ZoneCells != null && plan.ZoneCells.Count > 0;
            if (plan.ZoneCells != null)
                foreach (var c in plan.ZoneCells)
                {
                    int key = c.State == ZoneCellState.Blocked ? BlockedCells : c.State == ZoneCellState.Kept ? KeptCells : ZoneColorKey(c.ZoneId);
                    float y = c.Height;
                    if (c.Clipped != null && c.Clipped.Length >= 3)
                    {
                        var r3 = new float3[c.Clipped.Length];
                        for (int i = 0; i < r3.Length; i++) r3[i] = new float3(c.Clipped[i].x, y, c.Clipped[i].y);
                        Group(_zoneCellGroups, key).Add(r3);
                    }
                    else Group(_zoneCellGroups, key).Add(new[] { new float3(c.A.x, y, c.A.y), new float3(c.B.x, y, c.B.y), new float3(c.C.x, y, c.C.y), new float3(c.D.x, y, c.D.y) });
                }
            // Without captured road blocks (roads this build adds) the blocking is shown as its outline.
            if ((plan.ZoneCells == null || plan.ZoneCells.Count == 0) && plan.ZoneBlockers != null)
            {
                float y = 0f;
                if (_tool.Shape?.Ring != null && _tool.Shape.Ring.Length > 0) y = _tool.Shape.Ring[0].y;
                foreach (var r in plan.ZoneBlockers)
                {
                    var r3 = new float3[r.Length];
                    for (int i = 0; i < r.Length; i++) r3[i] = new float3(r[i].x, y, r[i].y);
                    _zoneBlockerRings.Add(r3);
                }
            }
        }

        private static int ZoneColorKey(string zoneId)
        {
            var id = zoneId ?? "";
            if (id.IndexOf("Residential", StringComparison.OrdinalIgnoreCase) >= 0) return 1;
            if (id.IndexOf("Office", StringComparison.OrdinalIgnoreCase) >= 0) return 2;
            if (id.IndexOf("Industrial", StringComparison.OrdinalIgnoreCase) >= 0) return 3;
            return 0;
        }

        private static Color ZoneCellColor(int key)
        {
            switch (key)
            {
                case 1: return new Color(0.35f, 0.85f, 0.35f, 1f);   // residential green
                case 2: return new Color(0.70f, 0.50f, 1f, 1f);      // office purple
                case 3: return new Color(1f, 0.80f, 0.20f, 1f);      // industrial yellow
                case BlockedCells: return LayerColor(PreviewLegend.Blocked);
                case KeptCells: return new Color(1f, 1f, 1f, 1f);
                default: return LayerColor(PreviewLegend.Cutouts);   // commercial blue
            }
        }

        /// <summary>
        /// Cutouts: the game's zone colour per cell as a calm fill with a thin outline. Cells blocked for zoning (off
        /// by default): one faint tint clipped to the outline — no hatch, no outline. Shown also while the ghost
        /// hides the sketch (zoning has no ghost).
        /// </summary>
        private void DrawZoneCells(OverlayRenderSystem.Buffer buffer, float w, float alpha, bool isolating)
        {
            bool cutouts = Shows(PreviewLegend.Cutouts), blocked = Shows(PreviewLegend.Blocked);
            foreach (var kv in _zoneCellGroups)
            {
                var col = ZoneCellColor(kv.Key);
                bool isBlocked = kv.Key == BlockedCells, kept = kv.Key == KeptCells;
                bool on = isBlocked ? blocked : cutouts;
                float fill = isBlocked ? (isolating ? 0.16f : 0.09f) : kept ? 0f : 0.22f;
                _fill.SetLayer("zoneCells:" + kv.Key, kv.Value, WithAlpha(col, on ? fill * alpha : 0f), isBlocked ? -92.6f : -92.5f, FilledPreviewSystem.Signature(kv.Value));
                if (!on || isBlocked) continue;
                foreach (var r in kv.Value) Ring(buffer, r, WithAlpha(col, (kept ? 0.35f : 0.6f) * alpha), w * 0.25f);
            }
            if (blocked)
                foreach (var r in _zoneBlockerRings) Ring(buffer, r, WithAlpha(ZoneCellColor(BlockedCells), 0.4f * alpha), w * 0.3f, dashed: true);
        }

        /// <summary>
        /// Rasterises the plan's texture paints coarsely (4 m cells, bounded budget: this runs on the main thread once
        /// per plan) and groups the cells into rectangles per material and strength level.
        /// </summary>
        private void BuildPaintPreview(ComposedPlan plan)
        {
            _paintGroups.Clear();
            var paints = new List<PlannedPaint>();
            foreach (var l in plan.Layers)
                if (l?.Paint != null)
                    foreach (var p in l.Paint) if (p?.Ring != null && p.Ring.Length >= 3) paints.Add(p);
            if (paints.Count == 0) return;
            try
            {
                var stamps = PaintRaster.Build(paints, new PaintRasterOptions { Cell = 2f, Align = 2f, TileCells = 256, MaxStamps = 64, MaxCells = 1 << 18, MaxCell = 16f });
                foreach (var (layer, level, ring) in PaintRaster.PreviewRects(stamps, PaintLevels, 4f, 3000))
                {
                    var r3 = new float3[ring.Length];
                    for (int i = 0; i < ring.Length; i++) r3[i] = new float3(ring[i].x, 0f, ring[i].y);
                    Group(_paintGroups, layer * 16 + level).Add(r3);
                }
            }
            catch (Exception e)
            {
                Mod.Log.Warn("Placemaker paint preview failed: " + e.Message);
                _paintGroups.Clear();
            }
        }

        /// <summary>
        /// Preview tint of a paint layer: the map's real material colour (s33 TerrainPaletteReader; vanilla: dry grass,
        /// forest floor, light dirt, grey sand), lifted and saturated so it reads as an overlay on the terrain. 0 = erase.
        /// </summary>
        private static Color PaintColor(int layer)
        {
            if (layer <= 0) return new Color(0.70f, 0.80f, 0.60f, 1f);
            var p = Placemaker.Core.Paint.TerrainPalette.OrVanilla(Placemaker.Game.Preview.TerrainPaletteReader.Last);
            var c = p.ColorOf(layer);
            float grey = (c.x + c.y + c.z) / 3f;
            var sat = new Unity.Mathematics.float3(grey) + (c - grey) * 2.2f;
            var lifted = Unity.Mathematics.math.saturate(Unity.Mathematics.math.lerp(sat, new Unity.Mathematics.float3(1f), 0.3f) * 1.35f);
            return new Color(lifted.x, lifted.y, lifted.z, 1f);
        }


        private static List<float3[]> Group(Dictionary<int, List<float3[]>> d, int key)
        {
            if (!d.TryGetValue(key, out var l)) d[key] = l = new List<float3[]>();
            return l;
        }

        private void DrawErrors(OverlayRenderSystem.Buffer buffer, float w)
        {
            // (s53) Hovering a "Left out" reason in the panel: where those pieces would have been.
            var auditHover = _tool.AuditHover;
            if (!string.IsNullOrEmpty(auditHover) && _tool.BuildAudit != null)
                foreach (var p in _tool.BuildAudit.Positions(auditHover))
                    buffer.DrawCircle(Warning, new Color(1f, 0.7f, 0.1f, 0.25f), w, 0, new float2(0, 1), p, w * 9f);
            // What the last build skipped (amber) or repaired (blue), until the player changes something.
            foreach (var n in _tool.BuildNotes)
                if (n?.At != null)
                    buffer.DrawCircle(n.Severity == IssueSeverity.Warning ? Warning : new Color(0.4f, 0.7f, 1f, 0.9f), new Color(1f, 1f, 1f, 0.1f), w, 0, new float2(0, 1), n.At.Value, w * 14f);
            var errors = _tool.BuildErrorPositions;
            if (errors == null) return;
            foreach (var p in errors) buffer.DrawCircle(Error, new Color(1f, 0f, 0f, 0.2f), w, 0, new float2(0, 1), p, w * 16f);
        }

        private static Color WithAlpha(Color c, float a) => new Color(c.r, c.g, c.b, a);

        private static Color KindColor(string kind, float a)
        {
            var (r, g, b) = MarkerKinds.ColorFor(kind);
            return new Color(r, g, b, a);
        }

        // ───────────── plan: nets and objects (sketch style) ─────────────
        //
        // Grid Road Generator's sketch look: a thin dashed centre line in the role colour plus two faint
        // edge lines at the real width — readable over terrain and never hiding it. Line width and dash
        // length follow the camera height (w), so it stays crisp close up and visible from far away.

        private Dictionary<string, AssetInfo> _assetInfo;

        private float HalfWidth(PlannedNet n)
        {
            if (_assetInfo != null && !string.IsNullOrEmpty(n.Asset.Id) && _assetInfo.TryGetValue(n.Asset.Id, out var info) && info.NetWidth > 0.5f)
                return info.NetWidth * 0.5f;
            return Composer.DefaultHalfWidth(n.Role);
        }

        private void DrawNet(OverlayRenderSystem.Buffer buffer, PlannedNet n, float w, float alpha)
        {
            var curve = new Bezier4x3(n.A, n.B, n.C, n.D);
            var col = NetColor(n.Role);
            if (n.Role == NetRole.Fence)
            {
                buffer.DrawCurve(WithAlpha(col, 0.7f * alpha), curve, w * 0.6f);
                return;
            }
            float hw = HalfWidth(n);
            // Edges: faint, at the real width (skipped when narrower than the centre line would make them readable).
            if (hw > w * 1.5f)
            {
                var edge = WithAlpha(col, 0.3f * alpha);
                buffer.DrawCurve(edge, OffsetCurve(curve, hw), w * 0.45f);
                buffer.DrawCurve(edge, OffsetCurve(curve, -hw), w * 0.45f);
            }
            float dash = w * 5f;
            buffer.DrawDashedCurve(WithAlpha(col, 0.95f * alpha), curve, w * 0.8f, dash, dash * 0.7f);
        }

        /// <summary>
        /// Zebra stripes where a footpath end is attached to a city street (an entrance placed with a crosswalk,
        /// Core/Layers/Common/StreetJoins): across the carriageway, each stripe along the street, plus a dashed leg from the
        /// path's end to the street's centre line (the game carries it there).
        /// </summary>
        private void DrawCrosswalks(OverlayRenderSystem.Buffer buffer, ComposedPlan plan, float w, float alpha)
        {
            if (plan.ExistingNets == null || plan.ExistingNets.Count == 0) return;
            var white = new Color(1f, 1f, 1f, 0.9f * alpha);
            foreach (var l in plan.Layers)
            {
                if (l?.Nets == null) continue;
                foreach (var n in l.Nets)
                {
                    if (n == null || (n.Role != NetRole.Pathway && n.Role != NetRole.PathwayWide)) continue;
                    for (int end = 0; end < 2; end++)
                    {
                        long id = end == 0 ? n.StartAttach : n.EndAttach;
                        if (id < 0) continue;
                        NearbyNet road = default; bool found = false;
                        foreach (var r in plan.ExistingNets) if (r.ExternalId == id && r.Category == NetCategory.Road) { road = r; found = true; break; }
                        if (!found) continue;
                        var tip = end == 0 ? n.A : n.D;
                        var rc = new Bezier4x3(road.A, road.B, road.C, road.D);
                        MathUtils.Distance(rc.xz, tip.xz, out float t);
                        var centre = MathUtils.Position(rc, t);
                        var along = math.normalizesafe(MathUtils.Tangent(rc, t).xz, new float2(1, 0));
                        var across = new float2(-along.y, along.x);
                        // the carriageway: the street's width less the sidewalks (about a quarter of it on each side of a city street)
                        float half = math.max(2f, road.Width * 0.5f * (road.HasSidewalk ? 0.7f : 0.95f));
                        int stripes = math.clamp((int)math.round(half * 2f / 1.2f), 3, 30);
                        float stripe = math.max(0.35f, w * 0.9f), len = 2.6f;
                        for (int k = 0; k < stripes; k++)
                        {
                            float off = -half + (k + 0.5f) * (half * 2f / stripes);
                            var mid = centre.xz + across * off;
                            var a = OnGround(mid - along * len * 0.5f);
                            var b = OnGround(mid + along * len * 0.5f);
                            buffer.DrawLine(white, new Line3.Segment(a, b), stripe);
                        }
                        buffer.DrawDashedLine(WithAlpha(NetColor(n.Role), 0.9f * alpha), new Line3.Segment(OnGround(tip.xz), OnGround(centre.xz)), w * 0.8f, w * 3f, w * 2f);
                    }
                }
            }
        }

        /// <summary>Approximate parallel curve: end points and handles shifted along the end normals (fine for road-like curvature).</summary>
        private static Bezier4x3 OffsetCurve(Bezier4x3 c, float d)
        {
            float2 t0 = math.normalizesafe(c.b.xz - c.a.xz, math.normalizesafe(c.d.xz - c.a.xz, new float2(1, 0)));
            float2 t1 = math.normalizesafe(c.d.xz - c.c.xz, t0);
            float3 n0 = new float3(-t0.y, 0, t0.x) * d, n1 = new float3(-t1.y, 0, t1.x) * d;
            return new Bezier4x3(c.a + n0, c.b + n0, c.c + n1, c.d + n1);
        }

        // ───────────── objects: one clear symbol per role ─────────────
        //
        // FEEDBACK-4 #4 / 4b #9: tree = soft crown at real crown size with a darker centre; bush = small
        // filled disc; hedge = short bar along the piece; bench = brown bar; lamp = yellow disc with a dark
        // rim; bin = dark disc with a light rim; centerpiece = ring; amenities/furniture (playground, picnic,
        // restroom, kiosk, field props…) = filled footprint in a category colour; parking bay = outline;
        // decal = faint disc. Every symbol has a minimum size tied to the line width, so it stays legible.

        private readonly Dictionary<string, AssetInfo> _categoryInfo = new Dictionary<string, AssetInfo>(StringComparer.Ordinal);

        private static bool IsClutter(PlannedObject o) =>
            o.Role == ObjectRole.Decal || o.Role == ObjectRole.Planter || (o.Role == ObjectRole.Bush && !IsHedge(o));

        private static bool IsHedge(PlannedObject o)
        {
            var c = o.Category;
            return !string.IsNullOrEmpty(c) && c.StartsWith(AssetCategories.Hedge, StringComparison.Ordinal);
        }

        /// <summary>Asset metadata of the object: its own (resolved) asset, else the category's automatic default (Design stage).</summary>
        private bool TryInfo(PlannedObject o, out AssetInfo info)
        {
            info = default;
            if (_assetInfo == null) return false;
            if (!string.IsNullOrEmpty(o.Asset.Id) && _assetInfo.TryGetValue(o.Asset.Id, out info)) return true;
            string cat = RoleCategories.ForObject(o) ?? "";
            if (_categoryInfo.TryGetValue(cat, out info)) return info.Id != null;
            info = default;
            try
            {
                var pool = (Services.Assets as global::Placemaker.Game.Assets.AssetCatalogSystem)?.AutomaticPool(cat);
                if (pool != null)
                    foreach (var id in pool)
                        if (id != null && _assetInfo.TryGetValue(id, out info)) break;
            }
            catch (Exception) { info = default; }
            _categoryInfo[cat] = info;
            return info.Id != null;
        }

        /// <summary>Footprint half-extents (local X, Z) and centre offset, scaled.</summary>
        private (float2 half, float2 centre) Footprint(PlannedObject o, float scale)
        {
            if (TryInfo(o, out var info))
            {
                var size = (info.BoundsMax - info.BoundsMin).xz;
                if (math.cmax(size) > 0.1f)
                    return (size * 0.5f * scale, (info.BoundsMax + info.BoundsMin).xz * 0.5f * scale);
            }
            float r = Composer.DefaultRadius(o.Role) * scale;
            return (new float2(r), float2.zero);
        }

        /// <summary>
        /// Crown radius: CS2 tree bounds are close to the trunk/collision box, so the visible crown is
        /// estimated from the height too (about 0.28 × height), never smaller than the bounds.
        /// </summary>
        internal static float CrownRadius(float3 boundsMin, float3 boundsMax, float scale)
        {
            float r = math.cmax((boundsMax - boundsMin).xz) * 0.5f;
            float h = boundsMax.y - boundsMin.y;
            return math.clamp(math.max(r, h * 0.28f), 1f, 7f) * scale;
        }

        private void DrawObject(OverlayRenderSystem.Buffer buffer, PlannedObject o, float w, float alpha)
        {
            float scale = math.max(0.3f, o.Scale);
            string layer = PreviewLegend.Of(o);
            // s22: one colour per legend layer (what the legend swatch shows), softer and smaller symbols.
            var col = layer == PreviewLegend.Objects || layer == PreviewLegend.Features ? LayerColor(layer) : ObjectColor(o.Role, RoleCategories.ForObject(o) ?? "");
            var p = o.Position;
            var up = new float2(0, 1);
            var none = new Color(0, 0, 0, 0);
            switch (o.Role)
            {
                case ObjectRole.Tree:
                {
                    // Soft crown at its real size: faint fill, thin rim, small trunk dot.
                    float crown = TryInfo(o, out var info) ? CrownRadius(info.BoundsMin, info.BoundsMax, scale) : Composer.DefaultRadius(o.Role) * scale;
                    float dia = math.max(crown * 2f, w * 4f);
                    buffer.DrawCircle(WithAlpha(col * 0.8f, 0.45f * alpha), WithAlpha(col, 0.10f * alpha), w * 0.22f, 0, up, p, dia);
                    buffer.DrawCircle(WithAlpha(col * 0.6f, 0.6f * alpha), p, math.max(dia * 0.16f, w * 0.9f));
                    return;
                }
                case ObjectRole.Centerpiece:
                {
                    var (half, _) = Footprint(o, scale);
                    float dia = math.max(math.cmax(half) * 2f, w * 6f);
                    buffer.DrawCircle(WithAlpha(col, 0.85f * alpha), WithAlpha(col, 0.10f * alpha), w * 0.6f, 0, up, p, dia);
                    buffer.DrawCircle(WithAlpha(col, 0.9f * alpha), p, w * 1.0f);
                    return;
                }
                case ObjectRole.Lamp:
                case ObjectRole.Bin:
                    // Small furniture: one small soft dot (they were the yellow and dark dots all over the park).
                    buffer.DrawCircle(WithAlpha(col, 0.65f * alpha), p, math.max(w * (o.Role == ObjectRole.Lamp ? 1.4f : 1.1f), 0.5f));
                    return;
                case ObjectRole.Decal:
                {
                    var (half, _) = Footprint(o, scale);
                    buffer.DrawCircle(WithAlpha(col, 0.22f * alpha), p, math.max(math.cmin(half) * 2f, w * 1.2f));
                    return;
                }
                case ObjectRole.Marker: case ObjectRole.AccessPoint: case ObjectRole.AnimalSpawner:
                    buffer.DrawCircle(WithAlpha(col, 0.85f * alpha), none, w * 0.35f, 0, up, p, w * 2.2f);
                    return;
                case ObjectRole.Bush: case ObjectRole.Planter:
                    if (!IsHedge(o))
                    {
                        var (half, _) = Footprint(o, scale);
                        buffer.DrawCircle(WithAlpha(col, 0.45f * alpha), p, math.max(math.cmax(half) * 1.6f, w * 1.4f));
                        return;
                    }
                    break;
            }
            DrawFootprint(buffer, o, scale, col, w, layer == PreviewLegend.Features ? alpha : alpha * 0.75f, outlineOnly: o.Role == ObjectRole.ParkingBay);
        }

        /// <summary>Rotated footprint: a bar along its long axis (DrawLine width = the short side), or its outline.</summary>
        private void DrawFootprint(OverlayRenderSystem.Buffer buffer, PlannedObject o, float scale, Color col, float w, float alpha, bool outlineOnly)
        {
            var (half, centre) = Footprint(o, scale);
            half = math.max(half, new float2(w * 0.9f));
            float c = math.cos(o.Yaw), s = math.sin(o.Yaw);
            var pos = o.Position;
            float3 Rot(float2 v) => new float3(v.x * c + v.y * s, 0f, -v.x * s + v.y * c) + pos;
            if (outlineOnly)
            {
                var corners = new[] { Rot(centre - half), Rot(centre + new float2(half.x, -half.y)), Rot(centre + half), Rot(centre + new float2(-half.x, half.y)) };
                Ring(buffer, corners, WithAlpha(col, 0.6f * alpha), w * 0.35f);
                return;
            }
            bool alongX = half.x >= half.y;
            float2 axis = alongX ? new float2(half.x, 0) : new float2(0, half.y);
            float width = (alongX ? half.y : half.x) * 2f;
            buffer.DrawLine(WithAlpha(col, 0.9f * alpha), new Line3.Segment(Rot(centre - axis), Rot(centre + axis)), width);
        }

        private static Color NetColor(NetRole r)
        {
            switch (r)
            {
                // (s16) Roads by role, in the colours of the panel's road legend (RoadRoles.Color): white streets,
                // cyan avenues, green locals, violet roundabout rings, orange perimeter, warm-yellow car-free/paths.
                case NetRole.Avenue: case NetRole.Street: case NetRole.PerimeterRoad: case NetRole.CulDeSac: case NetRole.Roundabout:
                case NetRole.Pathway: case NetRole.PathwayWide:
                    var c = Placemaker.Core.Layers.Roads.RoadRoles.Rgb(Placemaker.Core.Layers.Roads.RoadRoles.Of(r));
                    return new Color(c.x, c.y, c.z);
                case NetRole.DriveAisle: case NetRole.DriveConnector: case NetRole.Alley: return new Color(0.4f, 0.6f, 1f);   // parking aisles: blue
                case NetRole.Fence: return new Color(0.6f, 0.45f, 0.32f);
                default: return Color.white;
            }
        }

        private static Color ObjectColor(ObjectRole r, string cat)
        {
            if (cat.StartsWith(AssetCategories.Hedge, StringComparison.Ordinal)) return new Color(0.2f, 0.5f, 0.2f);
            if (cat.StartsWith(AssetCategories.Flowerbed, StringComparison.Ordinal)) return new Color(0.95f, 0.5f, 0.8f);
            if (cat.StartsWith("play", StringComparison.Ordinal)) return new Color(1f, 0.55f, 0.2f);          // playground, play.*
            if (cat.StartsWith(AssetCategories.Sports, StringComparison.Ordinal) || cat.StartsWith("dog.", StringComparison.Ordinal)) return new Color(1f, 0.4f, 0.4f);
            if (cat.StartsWith(AssetCategories.Picnic, StringComparison.Ordinal) || cat.StartsWith(AssetCategories.CafeSeating, StringComparison.Ordinal)) return new Color(0.85f, 0.55f, 0.3f);
            if (cat.StartsWith(AssetCategories.Kiosk, StringComparison.Ordinal) || cat.StartsWith(AssetCategories.Restroom, StringComparison.Ordinal)
                || cat.StartsWith(AssetCategories.Shelter, StringComparison.Ordinal) || cat.StartsWith("camping", StringComparison.Ordinal)) return new Color(0.95f, 0.7f, 0.35f);
            if (cat.StartsWith("memorial", StringComparison.Ordinal) || cat.StartsWith("monument", StringComparison.Ordinal) || cat.StartsWith("cemetery", StringComparison.Ordinal))
                return new Color(0.7f, 0.65f, 1f);
            if (cat.StartsWith(AssetCategories.Rock, StringComparison.Ordinal)) return new Color(0.7f, 0.7f, 0.7f);
            switch (r)
            {
                case ObjectRole.Tree: return new Color(0.25f, 0.75f, 0.3f);
                case ObjectRole.Bush: return new Color(0.5f, 0.85f, 0.35f);
                case ObjectRole.Planter: return new Color(0.8f, 0.6f, 0.45f);
                case ObjectRole.Bench: return new Color(0.75f, 0.45f, 0.2f);
                case ObjectRole.Bin: return new Color(0.2f, 0.2f, 0.22f);
                case ObjectRole.Lamp: return new Color(1f, 0.85f, 0.2f);
                case ObjectRole.Centerpiece: return new Color(0.4f, 0.75f, 1f);
                case ObjectRole.Decal: return new Color(0.75f, 0.65f, 0.5f);
                case ObjectRole.ParkingBay: case ObjectRole.Arrow: return new Color(0.9f, 0.9f, 1f);
                case ObjectRole.Charger: return new Color(0.3f, 0.9f, 0.5f);
                case ObjectRole.Marker: case ObjectRole.AccessPoint: case ObjectRole.AnimalSpawner: return new Color(0.95f, 0.4f, 0.95f);
                default: return new Color(0.75f, 0.7f, 1f);                                                   // other furniture: lilac
            }
        }

        /// <summary>Fill alpha per surface role (at most 15 %): the tint only says "what", the outline says "where".</summary>
        private static float SurfaceTint(SurfaceRole r) =>
            r == SurfaceRole.Water ? 0.15f : r == SurfaceRole.Grass ? 0.08f : 0.11f;

        private static Color SurfaceColor(SurfaceRole r)
        {
            switch (r)
            {
                case SurfaceRole.Grass: return new Color(0.35f, 0.75f, 0.3f);
                case SurfaceRole.Paving: return new Color(0.85f, 0.8f, 0.7f);
                case SurfaceRole.Plaza: return new Color(1f, 0.85f, 0.55f);
                case SurfaceRole.Asphalt: return new Color(0.35f, 0.35f, 0.4f);
                case SurfaceRole.Water: return new Color(0.25f, 0.5f, 0.95f);
                case SurfaceRole.Decorative: return new Color(0.85f, 0.55f, 0.75f);
                default: return new Color(0.6f, 0.5f, 0.35f);
            }
        }

        private static Color AreaColor(AreaRole r) =>
            r == AreaRole.Lot ? new Color(0.9f, 0.9f, 1f) : r == AreaRole.Hangaround ? new Color(1f, 0.85f, 0.5f) : new Color(0.7f, 0.9f, 1f);

        // ───────────── manage ─────────────

        private void DrawManage(OverlayRenderSystem.Buffer buffer, float w)
        {
            int group = _tool.ManagedGroup != 0 ? _tool.ManagedGroup : _tool.HoverGroup;
            if (group == 0) return;
            var em = EntityManager;
            var members = _tool.GroupMembers(group);
            int budget = 5000;
            var c = _tool.ManagedGroup != 0 ? Manage : new Color(1f, 1f, 1f, 0.75f);
            foreach (var e in members)
            {
                if (budget-- <= 0) break;
                if (e == Entity.Null || !em.Exists(e)) continue;
                if (em.HasComponent<Curve>(e)) { buffer.DrawCurve(c, em.GetComponentData<Curve>(e).m_Bezier, w * 2f); continue; }
                if (em.HasBuffer<global::Game.Areas.Node>(e))
                {
                    var nodes = em.GetBuffer<global::Game.Areas.Node>(e, true);
                    for (int i = 0; i < nodes.Length; i++)
                        buffer.DrawLine(c, new Line3.Segment(nodes[i].m_Position, nodes[(i + 1) % nodes.Length].m_Position), w);
                    continue;
                }
                if (em.HasComponent<global::Game.Objects.Transform>(e))
                    buffer.DrawCircle(c, em.GetComponentData<global::Game.Objects.Transform>(e).m_Position, w * 4f);
            }
        }

        [Preserve]
        public PlacemakerOverlaySystem() { }
    }
}
