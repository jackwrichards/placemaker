// What the panel and the cursor tooltips show: phase-aware hints and the contract's ToolState.
// One right-click rule everywhere: right-click removes the thing you would add with a click
// (corner, node, block, paint, marker) — never anything else.

using System.Collections.Generic;
using Placemaker.Core.Build;
using Placemaker.Core.Geometry;
using Placemaker.Core.Tool;

namespace Placemaker.Game.Tool
{
    public sealed partial class PlacemakerToolSystem
    {
        /// <summary>(Area overhaul) The legend rows for what the overlay shows while choosing the area.</summary>
        private List<PreviewLegend.Entry> AreaLegendNow()
        {
            var i = new AreaLegend.Input { Mode = _mode, HasShape = _shape != null, AreaM2 = _shape != null ? AreaM2 : 0 };
            switch (_mode)
            {
                case SelectionMode.Block:
                    i.HasFill = _hoverFace != null;
                    i.Doors = _hoverFace?.Doors.Count ?? 0;
                    i.Reach = _hoverFace?.Reach != null;
                    i.Stops = _hoverFace?.Stops.Count ?? 0;
                    break;
                case SelectionMode.BlockPaint:
                    var cur = BlockPaintCurrent;
                    i.HasFill = cur?.Ring != null || _hoverFace != null;
                    i.Doors = cur?.Doors.Count ?? _hoverFace?.Doors.Count ?? 0;
                    i.Stops = cur?.Obstacles.Count ?? _hoverFace?.Stops.Count ?? 0;
                    break;
                default:
                    i.LeftOut = _leftOut?.Count ?? 0;
                    i.Nodes = CornersOnNodes;
                    break;
            }
            return AreaLegend.Compute(i);
        }

        /// <summary>Hints for the current situation, most important first (tooltips show the first ones).</summary>
        public List<HintInfo> Hints()
        {
            var h = new List<HintInfo>(8);
            var flash = FlashText;
            if (flash != null) h.Add(new HintInfo(flash.Key) { Text = flash });

            if (_stage == ToolStage.Building)
            {
                h.Add(new HintInfo("Placemaker.Hint.Building"));
                return h;
            }
            if (_markerKind != null)
            {
                h.Add(new HintInfo("Placemaker.Hint.MarkerPlace"));
                h.Add(new HintInfo("Placemaker.Hint.MarkerRemove"));
                h.Add(new HintInfo("Placemaker.Hint.MarkerDone", "Esc"));
                return h;
            }
            if (_stage == ToolStage.Manage)
            {
                h.Add(new HintInfo("Placemaker.Hint.ManagePick"));
                if (_managedGroup != 0) h.Add(new HintInfo("Placemaker.Hint.ManageDeselect"));
                h.Add(new HintInfo("Placemaker.Hint.Back", "Esc"));
                return h;
            }

            int modeHints = h.Count;
            switch (_mode)
            {
                case SelectionMode.Draw:
                    if (!_draw.Closed)
                    {
                        h.Add(new HintInfo(_draw.Count >= PolygonEditor.MinPoints ? "Placemaker.Hint.DrawAddOrClose" : "Placemaker.Hint.DrawAdd"));
                        if (_lastSnap.Kind == SnapKind.RoadNode) h.Add(new HintInfo("Placemaker.Hint.DrawNodes"));
                        h.Add(new HintInfo("Placemaker.Hint.DrawNoSnap", "Alt"));
                    }
                    else
                    {
                        if (_gesture == Gesture.DragEdge) h.Add(new HintInfo("Placemaker.Hint.DrawEdgeDragging"));
                        else if (_hoverPoint >= 0)
                        {
                            h.Add(new HintInfo("Placemaker.Hint.DrawMovePoint"));
                            if (_stage != ToolStage.Area) h.Add(new HintInfo("Placemaker.Hint.DrawRemovePoint"));   // choosing the area: right-click starts over
                        }
                        else if (_hoverEdge >= 0)
                        {
                            h.Add(new HintInfo("Placemaker.Hint.DrawInsertDrag"));
                            h.Add(new HintInfo("Placemaker.Hint.DrawInsertClick", "Ctrl"));
                            h.Add(new HintInfo("Placemaker.Hint.DrawMoveEdge", "Shift"));
                        }
                        else h.Add(new HintInfo("Placemaker.Hint.DrawEdit"));
                    }
                    break;
                case SelectionMode.Block:
                    h.Add(new HintInfo(_blocks.Seeds.Count == 0 ? "Placemaker.Hint.BlockPick" : "Placemaker.Hint.BlockReplace"));
                    h.Add(new HintInfo("Placemaker.Hint.BlockAdd", "Shift"));
                    break;
                case SelectionMode.Perimeter:
                    if (!_perimeter.Closed)
                    {
                        h.Add(new HintInfo(_perimeter.Nodes.Count >= 2 ? "Placemaker.Hint.PerimeterAddOrClose" : "Placemaker.Hint.PerimeterAdd"));
                        if (_perimeter.Nodes.Count >= 3 && _shape != null) h.Add(new HintInfo("Placemaker.Hint.PerimeterAutoClose"));
                    }
                    break;
                case SelectionMode.Paint:
                    if (_stage != ToolStage.Area) break;   // s15: the brush only works in the Area stage
                    h.Add(new HintInfo("Placemaker.Hint.PaintPaint"));
                    h.Add(new HintInfo("Placemaker.Hint.PaintErase", "Shift"));
                    if (_brushSquare) h.Add(new HintInfo("Placemaker.Hint.PaintRotate", "Shift"));
                    break;
                case SelectionMode.BlockPaint:
                    if (_stage != ToolStage.Area) break;
                    var rules = BrushRules;
                    h.Add(new HintInfo(!rules.Stops(BrushObstacleKind.Road) ? "Placemaker.Hint.PaintPaint" : !BlockPaintHasTarget ? "Placemaker.Hint.BlockPaintStart" : "Placemaker.Hint.BlockPaintPaint"));
                    h.Add(new HintInfo("Placemaker.Hint.BlockPaintErase", "Shift"));
                    if (_brushSquare) h.Add(new HintInfo("Placemaker.Hint.PaintRotate", "Shift"));
                    if (BlockPaintCurrent?.Obstacles.Count > 0) h.Add(new HintInfo("Placemaker.Hint.BlockPaintBlocked"));
                    if (BlockPaintHasTarget && rules.Stops(BrushObstacleKind.Road)) h.Add(new HintInfo("Placemaker.Hint.BlockPaintNewBlock"));
                    break;
            }
            // Every area tool: right-click starts over while choosing the area (RightClickStartsOver); second, so the
            // footer (which shows the first few) never cuts it off.
            if (_stage == ToolStage.Area && HasSelection()) h.Insert(System.Math.Min(h.Count, modeHints + 1), new HintInfo("Placemaker.Hint.StartOver"));
            AddBuildingHint(h);
            if (_history.CanUndo) h.Add(new HintInfo("Placemaker.Hint.Undo", "Ctrl", "Z"));
            var p = StageMachine.Primary(_stage, Facts());
            if (p.Enabled && p.Kind != PrimaryKind.None) h.Add(new HintInfo("Placemaker.Hint.Primary", "Enter"));
            h.Add(new HintInfo(HasSelection() || _stage != ToolStage.Area ? "Placemaker.Hint.Back" : "Placemaker.Hint.Close", "Esc"));
            return h;
        }

        private LocText MarkerLabel(string path, string kind)
        {
            var step = RecipePaths.Resolve(_recipe, path);
            var gen = step != null ? Placemaker.Core.GeneratorRegistry.Get(step.Layer) : null;
            foreach (var k in MarkerKinds.For(gen))
                if (k.Kind == kind) return new LocText(k.LabelKey);
            return new LocText("Placemaker.Marker." + kind);
        }

        public ToolStateInput StateInput()
        {
            var facts = Facts();
            var s = new ToolStateInput
            {
                Open = IsActive || _building,
                Stage = _stage,
                Reachable = StageMachine.Reachable(facts),
                Mode = _mode,
                BrushSquare = _brushSquare,
                BrushSize = _brushSize,
                BrushAngle = _brushAngle,
                BrushRules = BrushRules,
                AreaValid = _shape != null && facts.AreaProblem == null,
                AreaClosed = AreaClosed,
                PointCount = PointCount,
                AreaM2 = AreaM2,
                PerimeterM = PerimeterM,
                AreaProblem = facts.AreaProblem,
                Primary = StageMachine.Primary(_stage, facts),
                CanUndo = CanUndo,
                CanRedo = CanRedo,
                UndoLabel = UndoLabel,
                RedoLabel = RedoLabel,
                MarkerStepPath = _markerPath,
                MarkerKind = _markerKind,
                MarkerLabel = _markerKind != null ? MarkerLabel(_markerPath, _markerKind) : null,
                MarkerCrosswalk = _markerCrosswalk,
                Hints = Hints(),
                Planning = facts.Planning,
                PlanningProgress = PlanningProgress,
                BuildProgress = _building ? BuildProgress : (float?)null,
                EditingGroup = _editingGroup,
                AnarchyAvailable = AnarchyAvailable,
                AnarchyEnabled = _anarchy,
                GroupingAvailable = _recipe != null && !Placemaker.Core.GeneratorRegistry.IsLoose(_recipe.Root?.Layer ?? ""),
                BuildSeparate = _buildUngrouped,
                KeepArea = _keepArea,
                PanelCollapsed = _panelCollapsed,
                PreviewMode = PreviewModeSelected,
                GhostPending = GhostPending,
                LegendVisibility = LegendVisibility,
                AreaLegend = _stage == ToolStage.Area ? AreaLegendNow() : null,
                Painter = PainterState(),
            };
            return s;
        }
    }
}
