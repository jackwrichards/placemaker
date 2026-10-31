using System;
using Game.Input;
using Game.Tools;
using Placemaker.Core.Tool;
using Placemaker.Game.Settings;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine.InputSystem;

namespace Placemaker.Game.Tool
{
    public sealed partial class PlacemakerToolSystem
    {
        private struct FrameInput
        {
            public bool HasHit;
            public float3 Hit;
            public Entity HitEntity;
            public global::Game.Common.RaycastHit RawHit;
            public bool OverWorld;
            public bool LeftPressed, LeftHeld, LeftReleased;
            public bool RightPressed, RightHeld;
            public bool Ctrl, Shift, Alt;
            public float2 MouseDelta;
            public float Time;
        }

        private FrameInput _in;

        private enum Gesture { None, DragPoint, InsertDrag, DragEdge, Paint, Erase }
        private Gesture _gesture;

        private ProxyAction _primaryAction, _undoAction, _redoAction, _redoAltAction;

        private void ReadInput()
        {
            if (SuiteInputOnly) { ReadSuiteInput(); return; }   // a suite run drives the tool (PlacemakerToolSystem.Suite)
            var im = InputManager.instance;
            var mouse = Mouse.current;
            var kb = Keyboard.current;
            _in = default;
            _in.Time = UnityEngine.Time.realtimeSinceStartup;
            _in.OverWorld = im != null && im.controlOverWorld && !im.hasInputFieldFocus;
            if (GetRaycastResult(out Entity e, out global::Game.Common.RaycastHit hit))
            {
                _in.HasHit = true;
                _in.HitEntity = e;
                _in.RawHit = hit;
                _in.Hit = hit.m_HitPosition;
                if (!math.all(math.isfinite(_in.Hit))) _in.HasHit = false;
            }
            _in.Ctrl = kb != null && (kb.leftCtrlKey.isPressed || kb.rightCtrlKey.isPressed);
            _in.Shift = kb != null && (kb.leftShiftKey.isPressed || kb.rightShiftKey.isPressed);
            _in.Alt = kb != null && (kb.leftAltKey.isPressed || kb.rightAltKey.isPressed);
            // Clicks: the game's Apply/Secondary actions (respect UI hover, gamepad) OR the raw mouse,
            // because CS2 masks unmodified ProxyActions while Ctrl/Shift/Alt are held (rule 19).
            bool rawLeftDown = mouse != null && mouse.leftButton.wasPressedThisFrame && _in.OverWorld;
            bool rawRightDown = mouse != null && mouse.rightButton.wasPressedThisFrame && _in.OverWorld;
            _in.LeftPressed = rawLeftDown || SafePressed(applyAction);
            _in.RightPressed = rawRightDown || SafePressed(secondaryApplyAction);
            _in.LeftHeld = (mouse != null && mouse.leftButton.isPressed) || SafeHeld(applyAction);
            _in.RightHeld = (mouse != null && mouse.rightButton.isPressed) || SafeHeld(secondaryApplyAction);
            _in.LeftReleased = !_in.LeftHeld;
            if (mouse != null) _in.MouseDelta = mouse.delta.ReadValue();
        }

        private static bool SafePressed(IProxyAction a)
        {
            try { return a != null && a.WasPressedThisFrame(); } catch (Exception) { return false; }
        }

        private static bool SafeHeld(IProxyAction a)
        {
            try { return a != null && a.IsPressed(); } catch (Exception) { return false; }
        }

        /// <summary>Esc, Enter and undo/redo. Returns true when the frame's input was consumed.</summary>
        private bool HandleKeys()
        {
            if (SuiteInputOnly) return HandleSuiteKey();   // a suite run: real keys are ignored, scripted keys take the same path
            var im = InputManager.instance;
            if (im != null && im.hasInputFieldFocus) return false;
            var mouse = Mouse.current;
            if (EscapeFromCancel(SafePressed(cancelAction), mouse != null && mouse.rightButton.wasPressedThisFrame)) { Escape(); return true; }
            if (Performed(_primaryAction)) { Primary(); return true; }
            if (_gesture == Gesture.None)
            {
                if (Performed(_undoAction)) { Undo(); return true; }
                if (Performed(_redoAction) || Performed(_redoAltAction)) { Redo(); return true; }
            }
            return false;
        }

        /// <summary>
        /// The game's tool "Cancel" is Esc, the gamepad's B and the right mouse button (its Tool map binds all three; the
        /// "Cancel" alias has every device). Only Esc and B step back: a right-click takes back the last thing placed, in
        /// each mode's own update (2026-10-22, the user: "Right-click goes back to the area mode... Right-click should be
        /// to remove the last thing that you just did").
        /// </summary>
        private static bool EscapeFromCancel(bool cancelPressed, bool rightMouseDown) => cancelPressed && !rightMouseDown;

        private static bool Performed(ProxyAction a)
        {
            try { return a != null && a.WasPerformedThisFrame(); } catch (Exception) { return false; }
        }

        private void EnableActions(bool on)
        {
            try
            {
                applyAction.shouldBeEnabled = on;
                secondaryApplyAction.shouldBeEnabled = on;
                cancelAction.shouldBeEnabled = on;
            }
            catch (Exception) { }
            var s = _settings;
            if (s == null) return;
            try
            {
                _primaryAction ??= s.GetAction(PlacemakerSettings.ActionPrimary);
                _undoAction ??= s.GetAction(PlacemakerSettings.ActionUndo);
                _redoAction ??= s.GetAction(PlacemakerSettings.ActionRedo);
                _redoAltAction ??= s.GetAction(PlacemakerSettings.ActionRedoAlt);
                if (_primaryAction != null) _primaryAction.shouldBeEnabled = on;
                if (_undoAction != null) _undoAction.shouldBeEnabled = on;
                if (_redoAction != null) _redoAction.shouldBeEnabled = on;
                if (_redoAltAction != null) _redoAltAction.shouldBeEnabled = on;
            }
            catch (Exception e) { Mod.Log.Warn("Placemaker: key actions unavailable: " + e.Message); }
        }

        /// <summary>World-space radius for grabbing handles: grows with camera distance so handles stay grabbable when zoomed out.</summary>
        private float HandleRadius(float3 at)
        {
            var cam = UnityEngine.Camera.main;
            if (cam == null) return 6f;
            float d = math.distance((float3)cam.transform.position, at);
            return math.clamp(d * 0.012f, 2.5f, 40f);
        }

        // ───────────── snapping ─────────────

        private SnapResult _lastSnap;
        public SnapResult LastSnap => _lastSnap;

        /// <summary>Snap a cursor position for draw mode. dragIndex = corner being dragged (-1 = adding).</summary>
        private float3 Snap(float3 raw, int dragIndex)
        {
            _lastSnap = new SnapResult { Position = raw };
            var mask = GetActualSnap();
            if (mask == global::Game.Tools.Snap.None || _in.Alt)   // Alt = place freely, like vanilla
                return WithHeight(raw);
            // (Area overhaul) Points snap to road nodes first (Road nodes is part of Points now): over a junction or a dead
            // end the corner sits on the node, and two such corners in a row follow the road between them.
            if ((mask & (global::Game.Tools.Snap.NetSide | global::Game.Tools.Snap.ExistingGeometry)) != 0 && _mode == SelectionMode.Draw)
            {
                var node = RoadNodeAt(raw.xz, HandleRadius(raw));
                if (node.id != 0)
                {
                    _lastSnap = new SnapResult { Position = node.pos, Kind = SnapKind.RoadNode, Node = node.id };
                    return node.pos;
                }
            }
            _resolver.SnapDistance = math.clamp(HandleRadius(raw) * 1.3f, 4f, 24f);
            _resolver.Begin(raw);
            ZoneSnapForDesign();
            if (_settings == null || _settings.SnapToRoadsWhenDrawing) _snapCollector.Collect(_resolver, mask);
            else _snapCollector.Collect(_resolver, mask & ~global::Game.Tools.Snap.NetSide);
            var pts = _draw.Points;
            if ((mask & global::Game.Tools.Snap.GuideLines) != 0) _resolver.OfferGuides(pts, _draw.Axes, dragIndex);
            if ((mask & global::Game.Tools.Snap.StraightDirection) != 0) _resolver.OfferDirections(pts, _draw.Axes, dragIndex, _draw.Closed);
            _lastSnap = _resolver.Result();
            _lastSnap.Position = WithHeight(_lastSnap.Position);
            return _lastSnap.Position;
        }

        /// <summary>
        /// (P2-1) With the rows on the zone grid (a step's "Row direction" set to "Zone grid"), outline corners snap to the zone
        /// cells as firmly as to roads, also away from any zoned cell: the extended grid of the nearest street's block (64 m).
        /// </summary>
        private void ZoneSnapForDesign()
        {
            bool zones = _recipe != null && UsesZoneRows(_recipe.Root);
            _snapCollector.ZoneLevel = zones ? SnapResolver.LevelNet : SnapResolver.LevelZoneGrid;
            _snapCollector.ZoneReach = zones ? 64f : 0f;
            if (zones != _zoneSnapLogged) { _zoneSnapLogged = zones; Mod.Log.Info("Placemaker: outline corners snap to the zone grid " + (zones ? "firmly (rows on the zone grid, 64 m reach)" : "as usual") + "."); }
        }

        private bool _zoneSnapLogged;

        private static bool UsesZoneRows(Placemaker.Core.RecipeStep step)
        {
            if (step == null || !step.Enabled) return false;
            if (step.Settings != null && step.Settings.Values.TryGetValue(Placemaker.Core.Layers.Parking.ParkingIds.RowAngleMode, out var v) && v as string == "zones") return true;
            foreach (var c in step.Children) if (UsesZoneRows(c)) return true;
            foreach (var o in step.RegionOverrides.Values) if (UsesZoneRows(o)) return true;
            return false;
        }

        private float3 WithHeight(float3 p) => new float3(p.x, _site.SampleHeight(p.xz), p.z);

        // ───────────── snap mask (vanilla panel) ─────────────

        /// <summary>What this tool can snap to — the vanilla snap panel shows exactly these toggles.</summary>
        private const Snap SupportedSnaps = global::Game.Tools.Snap.ExistingGeometry | global::Game.Tools.Snap.StraightDirection
            | global::Game.Tools.Snap.NetSide | global::Game.Tools.Snap.ObjectSide | global::Game.Tools.Snap.GuideLines | global::Game.Tools.Snap.ZoneGrid;

        public override void GetAvailableSnapMask(out Snap onMask, out Snap offMask)
        {
            if (_mode == SelectionMode.Draw && _stage != ToolStage.Manage && _stage != ToolStage.Building && _markerKind == null && _touchKind == null)
            {
                onMask = SupportedSnaps;
                offMask = SupportedSnaps;
            }
            else
            {
                onMask = global::Game.Tools.Snap.None;
                offMask = global::Game.Tools.Snap.None;
            }
        }

        private Snap _selectedSnap = global::Game.Tools.Snap.All;
        private bool _snapPersist;

        // CS2 writes selectedSnap when the player toggles the panel; it also resets it to "all" when
        // tools are (re)created, so the player's choice is saved here and restored on start.
        public override Snap selectedSnap
        {
            get => _selectedSnap;
            set
            {
                _selectedSnap = value;
                if (!_snapPersist || _settings == null) return;
                if (_settings.SnapMaskSaved && _settings.SnapMask == (int)value) return;
                _settings.SnapMask = (int)value;
                _settings.SnapMaskSaved = true;
                _settings.ApplyAndSave();
            }
        }

        private void RestoreSnapMask()
        {
            _snapPersist = false;
            if (_settings != null && _settings.SnapMaskSaved) _selectedSnap = (Snap)_settings.SnapMask;
            _snapPersist = true;
        }

        private void LockSnapMask() => _snapPersist = false;

        // ───────────── vanilla hint relabelling ─────────────
        // The bottom-right input hints show what Apply/Secondary do. Vanilla area tools swap their
        // apply action for named states ("Add Area Node", "Complete Area"…) whose bindings are the
        // same mouse buttons — we do the same so the hints read "Add corner", "Complete area" etc.

        private IProxyAction _hintAddNode, _hintInsertNode, _hintMoveNode, _hintDeleteNode, _hintComplete;
        private bool _hintsAvailable;

        private void CreateHintActions()
        {
            try
            {
                // InputManager.toolActionCollection is internal; the collection is a loaded ScriptableObject, so find it.
                UIInputActionCollection c = null;
                foreach (var candidate in UnityEngine.Resources.FindObjectsOfTypeAll<UIInputActionCollection>())
                {
                    if (candidate?.m_InputActions == null) continue;
                    foreach (var a in candidate.m_InputActions)
                        if (a != null && a.aliasName == "Add Area Node") { c = candidate; break; }
                    if (c != null) break;
                }
                if (c == null) return;
                string owner = nameof(PlacemakerToolSystem);
                _hintAddNode = c.GetActionState("Add Area Node", owner);
                _hintInsertNode = c.GetActionState("Insert Area Node", owner);
                _hintMoveNode = c.GetActionState("Move Area Node", owner);
                _hintDeleteNode = c.GetActionState("Delete Area Node", owner);
                _hintComplete = c.GetActionState("Complete Area", owner);
                _hintsAvailable = true;
            }
            catch (Exception e)
            {
                _hintsAvailable = false;
                Mod.Log.Info("Placemaker: vanilla hint actions unavailable (" + e.Message + "); keeping default hints");
            }
        }

        private void UpdateHintActions()
        {
            if (!_hintsAvailable || !IsActive) return;
            try
            {
                IProxyAction apply = null, secondary = null;
                // Never swap mid-gesture: the new state would not see the press that started it.
                if (_gesture == Gesture.None && _markerKind == null && _touchKind == null && _mode == SelectionMode.Draw
                    && (_stage == ToolStage.Area || _stage == ToolStage.Design || _stage == ToolStage.Review))
                {
                    // (Right-click starts over while choosing the area: no vanilla words for that, so no secondary hint then.)
                    if (!_draw.Closed) apply = _hoverClose ? _hintComplete : _hintAddNode;
                    else if (_hoverPoint >= 0) { apply = _hintMoveNode; if (_stage != ToolStage.Area) secondary = _hintDeleteNode; }
                    else if (_hoverEdge >= 0) apply = _hintInsertNode;
                }
                applyActionOverride = apply;
                secondaryApplyActionOverride = secondary;
                applyAction.shouldBeEnabled = true;
                secondaryApplyAction.shouldBeEnabled = true;
            }
            catch (Exception e)
            {
                _hintsAvailable = false;
                Mod.Log.Info("Placemaker: disabling vanilla hint relabelling: " + e.Message);
            }
        }
    }
}
