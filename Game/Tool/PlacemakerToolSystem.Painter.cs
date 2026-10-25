// Placemaker — the neighbourhood-polish painter: "Polish gardens" in the Make row (GPL-3.0).
//
// Not a type: picked the way a type is picked, it turns the tool into a stamp for the lots' saved yes/no flag
// (Game/Neighbours/NeighbourPolishSystem.Lots). The lots are picked with the usual area tools (Points, Paint bucket,
// Paint area), fenced only by roads, paths, tracks and water (the lots themselves are what is picked); Polish stamps yes,
// Unpolish stamps no, on every lot whose middle lies in the area. The selection goes after the stamp: only the flags stay,
// nothing is a lasting zone. While the painter is the pick, every polished lot is tinted green (PlacemakerOverlaySystem).
// Nothing is planned or built: no plan, no ghost, no Review.

using System;
using System.Collections.Generic;
using System.Linq;
using Placemaker.Core;
using Placemaker.Core.Geometry;
using Placemaker.Core.Neighbours;
using Placemaker.Core.Tool;
using Placemaker.Game.Neighbours;
using Unity.Entities;
using Unity.Mathematics;

namespace Placemaker.Game.Tool
{
    public sealed partial class PlacemakerToolSystem
    {
        /// <summary>The painter's Make-row entry (Recipes/shelf/neighbourhood.json lists it).</summary>
        public const string PainterId = "polishGardens";

        private bool _painter;
        private string _painterTheme = null;   // the look picked ("zone": each building its zone's look); null: the last one
        private Dictionary<string, float> _lookValues = new Dictionary<string, float>(PolishLooks.Defaults);
        private string _lookTheme = "garden";

        private string PainterLook
        {
            get
            {
                if (_painterTheme == null) SelectLook(global::Placemaker.Game.Tool.ToolRegistration.Settings?.PolishLookLast ?? "zone", remember: false);
                return _painterTheme;
            }
        }

        /// <summary>A look picked in the polish tool (its values become the rows'), or "zone".</summary>
        private void SelectLook(string id, bool remember = true)
        {
            var look = id == "zone" ? null : NeighbourPolishSystem.LookById(id);
            if (look == null && id != "zone") id = "zone";
            _painterTheme = id;
            _lookValues = look != null ? look.Values() : new Dictionary<string, float>(PolishLooks.Defaults);
            _lookTheme = look?.Theme ?? "garden";
            NeighbourPolishSystem.NoteReach(_lookValues);
            LiveChanged();
            var s = global::Placemaker.Game.Tool.ToolRegistration.Settings;
            if (remember && s != null && s.PolishLookLast != id) { s.PolishLookLast = id; s.ApplyAndSave(); }
            Touch();
        }

        private PolishLook CurrentLook => NeighbourPolishSystem.LookById(PainterLook);

        // Live polish (the switch beside Polish): the buildings in the selection are polished again a moment after every
        // change of the look or the selection; the selection stays (Polish clears it). Unpolish puts them back as ever.
        private bool _polishLive;
        private int _liveVersion, _liveDone = -1;
        private float _liveAt;
        private AreaShape _liveShape;
        private const float LiveDelay = 0.4f;

        public bool PolishLive => _polishLive;

        public void SetPolishLive(bool on)
        {
            if (on == _polishLive) return;
            _polishLive = on;
            LiveChanged();
            Touch();
        }

        // the editor's garden picture (Core GardenPreview): planned on a worker for the rows' values, again when they change
        private Placemaker.Core.Neighbours.GardenPreview.Picture _gardenPicture;
        private System.Threading.Tasks.Task<Placemaker.Core.Neighbours.GardenPreview.Picture> _gardenPictureJob;
        private Dictionary<string, float> _gardenPictureFor;

        /// <summary>The UI system, every frame (the tool itself updates only while it is open: the Global outgrow window's editor
        /// is there with it closed).</summary>
        public void PollGardenPreview() => UpdateGardenPreview();

        /// <summary>Every frame: the garden picture planned again once its last plan is done and the rows' values differ from
        /// what it was planned with (a slider dragged: the picture follows, one plan at a time).</summary>
        private void UpdateGardenPreview()
        {
            if (_gardenPictureJob != null)
            {
                if (!_gardenPictureJob.IsCompleted) return;
                if (_gardenPictureJob.Status == System.Threading.Tasks.TaskStatus.RanToCompletion)
                {
                    _gardenPicture = _gardenPictureJob.Result;
                    Touch();
                }
                else Mod.Log.Info($"Placemaker: the garden picture could not be planned: {_gardenPictureJob.Exception?.GetBaseException().Message}");
                _gardenPictureJob = null;
            }
            if (_gardenPictureFor != null && _gardenPictureFor.Count == _lookValues.Count)
            {
                bool same = true;
                foreach (var kv in _lookValues) if (!_gardenPictureFor.TryGetValue(kv.Key, out var v) || Math.Abs(v - kv.Value) >= 1e-4f) { same = false; break; }
                if (same) return;
            }
            var values = new Dictionary<string, float>(_lookValues);
            _gardenPictureFor = values;
            _gardenPictureJob = System.Threading.Tasks.Task.Run(() => Placemaker.Core.Neighbours.GardenPreview.Plan(values));
        }

        private void LiveChanged()
        {
            _liveVersion++;
            _liveAt = UnityEngine.Time.realtimeSinceStartup;
        }

        /// <summary>Every frame: the selection polished again once the look or the selection has settled for a moment.</summary>
        private void UpdateLivePolish()
        {
            if (!_painter || !_polishLive || _building) return;
            if (!ReferenceEquals(_liveShape, _shape)) { _liveShape = _shape; LiveChanged(); }
            if (_shape == null || _liveDone == _liveVersion || _gesture != Gesture.None || UnityEngine.Time.realtimeSinceStartup - _liveAt < LiveDelay) return;
            _liveDone = _liveVersion;
            var polish = World.GetExistingSystemManaged<NeighbourPolishSystem>();
            if (polish == null) return;
            var lots = PainterLots(polish);
            if (lots.Count == 0) return;
            NeighbourPolishSystem.Stamp(lots, true, PainterLook, LookChanges());
            LastStamp = (lots.Count, true);
            Mod.Log.Info($"Placemaker painter: live polish of {lots.Count} lots ({PainterLook}{(LookChanges() != null ? ", changed" : "")})");
        }

        /// <summary>What the rows changed from the picked look (null: nothing), "theme" its base theme's index.</summary>
        private Dictionary<string, float> LookChanges()
        {
            var look = CurrentLook;
            if (look == null) return null;
            var d = PolishLooks.Diff(_lookValues, look.Values());
            if (_lookTheme != look.Theme) d["theme"] = Array.IndexOf(PolishLooks.Themes, _lookTheme);
            return d.Count > 0 ? d : null;
        }

        /// <summary>For the game runner: a setting of the look as the rows have it; the changes Polish would give.</summary>
        public float PolishKnobValue(string id) => _lookValues.TryGetValue(id, out var v) ? v : float.NaN;
        public Dictionary<string, float> PolishChanges => LookChanges();

        /// <summary>A look row of the polish tool ("theme": the base theme's index).</summary>
        public void SetPolishKnob(string id, double value)
        {
            if (id == "theme") { int t = (int)Math.Round(value); if (t >= 0 && t < PolishLooks.Themes.Length) _lookTheme = PolishLooks.Themes[t]; }
            else if (PolishLooks.Defaults.ContainsKey(id)) _lookValues[id] = (float)value;
            else return;
            NeighbourPolishSystem.NoteReach(_lookValues);
            LiveChanged();
            Touch();
        }

        /// <summary>The look's buttons: "save" (over the look: the player's own, or a built-in one, which then is what that look
        /// is everywhere), "saveAs" (a new look named <paramref name="arg"/>), "rename" (the player's look to <paramref name="arg"/>),
        /// "delete" (the player's look), "revert" (back to its saved values), "reset" (a changed built-in look back as it ships).</summary>
        public void PolishLookAction(string what, string arg)
        {
            var look = CurrentLook;
            switch (what)
            {
                case "save":
                    if (look == null) return;
                    NeighbourPolishSystem.SaveLook(look.Id, look.Custom ? look.Name : null, _lookTheme, _lookValues);
                    SelectLook(look.Id);
                    break;
                case "reset":
                    if (look != null && look.Edited && NeighbourPolishSystem.ResetLook(look.Id)) SelectLook(look.Id);
                    break;
                case "color":
                    if (look != null && NeighbourPolishSystem.SetLookColor(look.Id, arg)) Touch();
                    break;
                case "saveAs":
                    var id = NeighbourPolishSystem.SaveLook(null, string.IsNullOrWhiteSpace(arg) ? "My look" : arg, _lookTheme, _lookValues);
                    SelectLook(id);
                    break;
                case "rename":
                    if (look != null && look.Custom && NeighbourPolishSystem.RenameLook(look.Id, arg)) Touch();
                    break;
                case "delete":
                    if (look != null && look.Custom && NeighbourPolishSystem.DeleteLook(look.Id))
                    {
                        var s = global::Placemaker.Game.Tool.ToolRegistration.Settings;
                        if (s != null) { s.PolishZoneLooks = NeighbourPolishSystem.ZoneLooksJson(); s.ApplyAndSave(); }
                        SelectLook("garden");
                    }
                    break;
                case "revert":
                    if (look != null) SelectLook(look.Id);
                    break;
            }
        }

        /// <summary>The City window: a zone type's look for new buildings and Polish the whole city ("off"; "" = as its kind).</summary>
        public void SetPolishZoneLook(string zone, string look)
        {
            if (!string.IsNullOrEmpty(look) && look != "off" && NeighbourPolishSystem.LookById(look) == null) return;
            NeighbourPolishSystem.SetZoneLook(zone, look);
            var s = global::Placemaker.Game.Tool.ToolRegistration.Settings;
            if (s != null) { s.PolishZoneLooks = NeighbourPolishSystem.ZoneLooksJson(); s.ApplyAndSave(); }
            Touch();
        }

        /// <summary>
        /// The polish tool's own button (next to Placemaker's): opens this tool as the polish painter, which the panel shows
        /// as its own small window (area tools, themes, Look, Whole city, Polish / Unpolish), or closes it. From Placemaker
        /// it switches over (Placemaker's own type and area stay for when its button is pressed again).
        /// </summary>
        public void TogglePolishTool()
        {
            // the toolbar's Outgrow button: the panel in its Outgrow mode (open), or closed when it already shows it
            if (m_ToolSystem == null || _building) return;
            bool active = m_ToolSystem.activeTool == this;
            if (active && _outgrowMode) { Close(); return; }
            SetOutgrowMode(true);
            if (!active) { _closeAfterGhost = 0; _closeAfterBuild = false; m_ToolSystem.activeTool = this; }
        }

        // The panel's two modes (its switcher, the toolbar's two buttons): Placemaker, or Outgrow (the whole city's outgrow:
        // defaults, the brush, the lens, types, numbers), which replaces the panel's stages; the area tools rest meanwhile
        private bool _outgrowMode;
        public bool OutgrowMode => _outgrowMode;

        public void SetOutgrowMode(bool on)
        {
            if (on && _painter) SetPainter(false);
            if (!on && _brush != 0) SetOutgrowBrush("off", null);
            if (_outgrowMode == on) { Touch(); return; }
            _outgrowMode = on;
            if (on) { CancelGesture(); _cursorValid = false; }
            Mod.Log.Info($"Placemaker panel: {(on ? "Outgrow" : "Placemaker")}");
            Touch();
        }

        /// <summary>For the game runner: the theme picked in the painter's Style row.</summary>
        public string PainterTheme => PainterLook;

        /// <summary>The theme the painter's Polish gives: "zone" (by each house's zone) or a theme name.</summary>
        public void SetPainterTheme(string theme)
        {
            if (string.IsNullOrEmpty(theme) || theme == PainterLook) return;
            SelectLook(theme);
        }
        private BrushRules _painterRules;
        private AreaShape _countShape;
        private readonly List<(string Key, string Zone, string Icon, int Count)> _areaZones = new List<(string, string, string, int)>();
        private int _countFlags = -1;
        private int _countLots, _countYes;

        /// <summary>The painter is the pick: the area is stamped, never built.</summary>
        public bool Painter => _painter;

        /// <summary>For the game runner: the last stamp (how many lots, yes or no).</summary>
        public (int Lots, bool Yes) LastStamp { get; private set; }

        /// <summary>Game runner: a scenario starts on its type, not on the painter a scenario before it left picked.</summary>
        public void SuiteLeavePainter() { if (SetPainter(false)) DesignChanged(); }

        /// <summary>Picks or leaves the painter. True when it changed.</summary>
        private bool SetPainter(bool on)
        {
            if (on == _painter) return false;
            CancelGesture();
            ExitMarkerMode();
            _painter = on;
            if (on && _painterRules == null) _painterRules = PainterRulesFrom(PlayerBrushRules);
            // other stops: the fills and the outline are worked out again
            _fills?.Invalidate();
            _hoverFace = null;
            if (_mode == SelectionMode.Block) RepickBlocks();
            else if (_mode == SelectionMode.Draw && _draw.Closed) ShapeChanged();
            // a painted area keeps what was painted and is fenced again with the painter's stops (lots and buildings no
            // longer stop it); lots the paint ran up against count as picked too (NeighbourPolishSystem.LotsInside)
            else if ((_mode == SelectionMode.Paint || _mode == SelectionMode.BlockPaint) && _shape != null) ShapeChanged();
            if (_stage == ToolStage.Review) SetStage(ToolStage.Design);   // Review is the other tool's: back to its Design
            _countShape = null;
            DesignChanged();
            Touch();
            return true;
        }

        /// <summary>The painter's stops: roads, paths, tracks and water as the player has them; never buildings, lots, zoning or builds.</summary>
        private static BrushRules PainterRulesFrom(BrushRules r)
        {
            var p = (r ?? BrushRules.Defaults()).Clone();
            p.Buildings = false;
            p.Lots = false;
            p.Builds = false;
            p.SetZoningLevel(0);
            p.Anything = false;
            p.DrawClip = false;
            return p.Normalized();
        }

        /// <summary>The panel's look rows (Polish gardens card): saved in the settings, given by Polish from now on and to new buildings.</summary>
        public void SetPolishOption(string id, double value)
        {
            var s = global::Placemaker.Game.Tool.ToolRegistration.Settings;
            if (s == null) return;
            bool on = value > 0.5;
            switch (id)
            {
                case "painted": s.PolishPaintedGardens = (int)Math.Round(Math.Max(0, Math.Min(100, value))); break;
                case "lawns": s.PolishLawns = on; break;
                case "planting": s.PolishPlanting = on; break;
                case "features": s.PolishFeatures = on; break;
                case "paths": s.PolishPaths = on; break;
                case "rocks": s.PolishRocks = on; break;
                case "groundPaint": s.PolishGroundPaint = on; break;
                case "shopPaving": s.PolishPavedFill = on; break;
                default: return;
            }
            s.ApplyAndSave();
            Touch();
        }

        /// <summary>
        /// The panel's Whole city rows: "on" / "newHouses" / "levelUps" / "gameChanges" / "gameRemovals" (value "true" / "false"), a kind's look for new buildings ("homes",
        /// "shops", "offices", "industry": "auto" each zone type its own, a look id, off), "polishAll" (every building with the Style picked
        /// and the look; By kind leaves out the kinds set to Off), "unpolishAll" (every garden back as the game made it). The
        /// city-wide two run in the background, a few buildings a frame; the panel asks first.
        /// </summary>
        public void PolishCity(string what, string value)
        {
            var s = global::Placemaker.Game.Tool.ToolRegistration.Settings;
            if (s == null) return;
            bool on = value == "true";
            switch (what)
            {
                // this city's switches (saved with it, never in the settings)
                case "on": NeighbourPolishSystem.SetRunning(on); Touch(); return;
                case "newHouses": NeighbourPolishSystem.SetNewBuildings(on); Touch(); return;
                case "levelUps": NeighbourPolishSystem.SetLevelUps(on); Touch(); return;
                case "gameChanges": NeighbourPolishSystem.SetGameChanges(on); Touch(); return;
                case "gameRemovals": NeighbourPolishSystem.SetGameRemovals(on); Touch(); return;
                case "homes": case "shops": case "offices": case "industry":
                    if (value != "off" && value != NeighbourPolishSystem.KindAuto && NeighbourPolishSystem.LookById(value) == null) return;
                    if (what == "homes") s.PolishThemeHomes = value;
                    else if (what == "shops") s.PolishThemeShops = value;
                    else if (what == "offices") s.PolishThemeOffices = value;
                    else s.PolishThemeIndustry = value;
                    break;
                case "polishAll":
                    NeighbourPolishSystem.PolishEverything(0);
                    Mod.Log.Info("Placemaker painter: polish the whole city (each building its zone's look)");
                    Flash(new LocText(NeighbourPolishSystem.Paused ? "Placemaker.Painter.Paused" : "Placemaker.Painter.CityPolishing"));
                    Touch();
                    return;
                case "unpolishAll":
                    NeighbourPolishSystem.RemoveAll();
                    Mod.Log.Info("Placemaker painter: unpolish the whole city");
                    Flash(new LocText("Placemaker.Painter.CityUnpolishing"));
                    Touch();
                    return;
                case "polishSome":
                    // one kind ("kind:home") or zone type ("zone:<prefab>") of the city
                    if (!s.NeighbourhoodPolish) s.NeighbourhoodPolish = true;
                    s.ApplyAndSave();
                    NeighbourPolishSystem.PolishEverything(0, value);
                    Mod.Log.Info($"Placemaker painter: outgrow {value} (each building its zone's look)");
                    Flash(new LocText("Placemaker.Painter.CityPolishing"));
                    Touch();
                    return;
                case "unpolishSome":
                    NeighbourPolishSystem.RemoveAll(value);
                    Mod.Log.Info($"Placemaker painter: remove outgrow from {value}");
                    Flash(new LocText("Placemaker.Painter.CityUnpolishing"));
                    Touch();
                    return;
                case "cityOpen":
                    // Outgrow shown (the panel's Outgrow mode): the area stays for Placemaker (hidden, its tools resting)
                    _cityOpen = on;
                    if (!on && _brush != 0) { _brush = 0; _brushDown = false; ClearBrushHits(); }
                    Touch();
                    return;
                case "mode": SetOutgrowMode(value == "outgrow"); return;
                case "lens": _lensWanted = on; Touch(); return;
                case "lensMode": _lensMode = value == "types" ? "types" : "status"; Touch(); return;
                case "brushSize":
                    if (float.TryParse(value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var bs)) SetOutgrowBrushWidth(bs);
                    return;
                case "brush":
                {
                    // "paint:<look>", "erase" or "off"
                    int colon = value.IndexOf(':');
                    SetOutgrowBrush(colon > 0 ? value.Substring(0, colon) : value, colon > 0 ? value.Substring(colon + 1) : null);
                    return;
                }
                default: return;
            }
            s.ApplyAndSave();
            Touch();
        }

        // Global outgrow's status lens: on while the window is open and its switch is on; its map: "status" or "types"
        private bool _cityOpen, _lensWanted = true;
        private string _lensMode = "status";
        /// <summary>The status lens is drawn (PlacemakerOverlaySystem).</summary>
        public bool OutgrowLens => _cityOpen && _lensWanted;
        /// <summary>Global outgrow's window is open: its lens switch alone decides what the map shows.</summary>
        public bool OutgrowCityOpen => _cityOpen;
        public string OutgrowLensMode => _lensMode;

        // Global outgrow's brush: a plain painter of its own, not an area tool (the panel's stage, mode and area are left as
        // they are). A stroke takes every building it touches (lit on the map); let go, they all get the outgrow type picked
        // (erase: put back) at the front of the line.
        private int _brush;
        private string _brushLook;
        private readonly HashSet<Entity> _brushHits = new HashSet<Entity>();
        private readonly List<float3[]> _brushRings = new List<float3[]>();
        private bool _brushDown;
        private float2 _brushLast;
        private float _brushWidth = 40f;

        public bool OutgrowBrushOn => _brush != 0;
        public bool OutgrowBrushPainting => _brush > 0;
        public string OutgrowBrushLook => _brushLook;
        /// <summary>The brush's diameter in metres.</summary>
        public float OutgrowBrushWidth => _brushWidth;
        /// <summary>The lots of the buildings the stroke has touched so far (the overlay lights them); bumped when they change.</summary>
        public IReadOnlyList<float3[]> OutgrowBrushRings => _brushRings;
        public int OutgrowBrushVersion { get; private set; }

        /// <summary>The brush on ("paint" with a look, "erase") or off ("").</summary>
        public void SetOutgrowBrush(string mode, string look)
        {
            _brush = mode == "paint" ? 1 : mode == "erase" ? -1 : 0;
            if (_brush > 0)
            {
                // "zone": every building painted gets its zone type's default outgrow type
                if (look != "zone" && NeighbourPolishSystem.LookById(look) == null) { _brush = 0; Touch(); return; }
                _brushLook = look;
            }
            _brushDown = false;
            ClearBrushHits();
            if (_brush != 0)
            {
                // the tool takes the mouse (the brush's strokes); Placemaker's area and stage stay as they are
                if (m_ToolSystem != null && m_ToolSystem.activeTool != this) { _closeAfterGhost = 0; m_ToolSystem.activeTool = this; }
                // the map lens shows what the brush changes: outgrow types while painting, outgrown or not while erasing
                _lensWanted = true;
                _lensMode = _brush > 0 ? "types" : "status";
                Mod.Log.Info($"Placemaker outgrow brush: {(_brush > 0 ? "paint " + look : "erase")}");
            }
            Touch();
        }

        public void SetOutgrowBrushWidth(float metres)
        {
            _brushWidth = math.clamp(metres, 4f, 300f);
            Touch();
        }

        /// <summary>
        /// The brush instead of the area tools, every frame while it is on: a press starts a stroke, every building it touches
        /// is taken (and lit), let go they all get the brush's outgrow type or lose their outgrow; a right-click drops the stroke.
        /// </summary>
        private void UpdateOutgrowBrushInput()
        {
            _cursorValid = _in.HasHit && _in.OverWorld;
            if (_cursorValid) _cursor = _in.Hit;
            if (!_cityOpen) { SetOutgrowBrush("off", null); return; }
            var polish = World.GetExistingSystemManaged<NeighbourPolishSystem>();
            if (polish == null) return;
            float r = _brushWidth * 0.5f;
            if (!_brushDown)
            {
                if (!_in.LeftPressed || !_cursorValid) return;
                _brushDown = true;
                ClearBrushHits();
                polish.BrushStart();
                _brushLast = _in.Hit.xz;
                if (polish.BrushTouch(_brushLast, _brushLast, r, _brushHits, _brushRings) > 0) OutgrowBrushVersion++;
                return;
            }
            if (_in.RightPressed) { _brushDown = false; ClearBrushHits(); return; }
            if (_in.LeftHeld)
            {
                if (_in.HasHit && math.distance(_in.Hit.xz, _brushLast) > math.max(1f, r * 0.2f))
                {
                    if (polish.BrushTouch(_brushLast, _in.Hit.xz, r, _brushHits, _brushRings) > 0) OutgrowBrushVersion++;
                    _brushLast = _in.Hit.xz;
                }
                return;
            }
            _brushDown = false;
            if (_brushHits.Count > 0)
            {
                var settings = global::Placemaker.Game.Tool.ToolRegistration.Settings;
                if (_brush > 0 && settings != null && !settings.NeighbourhoodPolish) { settings.NeighbourhoodPolish = true; settings.ApplyAndSave(); }
                NeighbourPolishSystem.Stamp(new List<Entity>(_brushHits), _brush > 0, _brush > 0 ? _brushLook : "zone", null);
                LastStamp = (_brushHits.Count, _brush > 0);
                Mod.Log.Info($"Placemaker outgrow brush: {_brushHits.Count} buildings {(_brush > 0 ? "outgrown as " + _brushLook : "put back")}");
            }
            ClearBrushHits();
        }

        private void ClearBrushHits()
        {
            if (_brushHits.Count == 0 && _brushRings.Count == 0) return;
            _brushHits.Clear();
            _brushRings.Clear();
            OutgrowBrushVersion++;
        }

        /// <summary>Polish (yes) or Unpolish (no) the lots in the area. The selection stays (press it again as often as wanted).</summary>
        public void PolishStamp(bool yes)
        {
            if (!_painter || _building) return;
            var polish = World.GetExistingSystemManaged<NeighbourPolishSystem>();
            if (polish == null) return;
            if (_shape == null) { Flash(new LocText("Placemaker.Reason.NoArea")); return; }
            var lots = PainterLots(polish);
            if (lots.Count == 0) { Flash(new LocText("Placemaker.Painter.NoLots")); return; }
            // paused (the master switch): the flags are kept and the lots outgrow once it runs again; it is never switched on here
            // "By zone": each lot gets its zone's look; a look picked: that look, and what the rows changed in it
            NeighbourPolishSystem.Stamp(lots, yes, PainterLook, yes ? LookChanges() : null);
            LastStamp = (lots.Count, yes);
            Mod.Log.Info($"Placemaker painter: {(yes ? "polish" : "unpolish")} {lots.Count} lots ({AreaM2:F0} m² picked with {SelectionModes.Id(_mode)})");
            // the selection stays: Polish again (another look, changed settings) or Unpolish whenever
            Flash(yes && NeighbourPolishSystem.Paused ? new LocText("Placemaker.Painter.Paused") : new LocText(yes ? "Placemaker.Painter.Polished" : "Placemaker.Painter.Unpolished", lots.Count));
        }

        /// <summary>The houses the painter acts on: in the area, or (Paint area) painted over by the brush.</summary>
        private List<Entity> PainterLots(NeighbourPolishSystem polish)
        {
            var lots = polish.LotsInside(_shape.Ring2D());
            if ((_mode == SelectionMode.BlockPaint || _mode == SelectionMode.Paint) && _bpBrushed.Count > 0)
            {
                var have = new HashSet<Entity>(lots);
                foreach (var e in polish.GrowablesNear(_shape.Ring2D(), 80f))
                    if (!have.Contains(e) && BrushedOver(EntityManager.GetComponentData<global::Game.Objects.Transform>(e).m_Position.xz)) { lots.Add(e); have.Add(e); }
            }
            return lots;
        }

        /// <summary>Lots in the current area and how many of them are polished (for the panel).</summary>
        public (int Lots, int Yes) PainterCount
        {
            get
            {
                if (!_painter && !_wasPolishType || _shape == null) return (0, 0);
                if (!ReferenceEquals(_countShape, _shape) || _countFlags != NeighbourPolishSystem.FlagsVersion)
                {
                    _countShape = _shape;
                    _countFlags = NeighbourPolishSystem.FlagsVersion;
                    _countLots = _countYes = 0;
                    _areaZones.Clear();
                    var polish = World.GetExistingSystemManaged<NeighbourPolishSystem>();
                    if (polish != null)
                    {
                        var byKey = new Dictionary<string, (string Zone, int Count)>();
                        foreach (var e in PainterLots(polish))
                        {
                            _countLots++;
                            if (polish.IsYes(e)) _countYes++;
                            string zone = polish.ZoneOfLot(e);
                            string key = NeighbourPolishSystem.ZoneKey(zone);
                            if (key == "") continue;
                            byKey[key] = (byKey.TryGetValue(key, out var z) ? z.Zone : zone, (byKey.TryGetValue(key, out var z2) ? z2.Count : 0) + 1);
                        }
                        var icons = new Dictionary<string, string>();
                        foreach (var zz in polish.Zones()) icons[zz.Id] = zz.Icon;
                        foreach (var kv in byKey.OrderByDescending(kv => kv.Value.Count))
                            _areaZones.Add((kv.Key, kv.Value.Zone, icons.TryGetValue(kv.Value.Zone, out var ic) ? ic : "", kv.Value.Count));
                    }
                }
                return (_countLots, _countYes);
            }
        }

        /// <summary>Outgrow as the panel shows it: always (the Global outgrow window and the looks are there whatever is picked);
        /// the area's counts only for the Outgrow tool and the Zoning types.</summary>
        /// <summary>Global outgrow's numbers as the panel shows them.</summary>
        private static PainterStats StatsView()
        {
            var st = NeighbourPolishSystem.Stats;
            var v = new PainterStats
            {
                KindTotal = (int[])st.KindTotal.Clone(), KindPolished = (int[])st.KindPolished.Clone(),
                ToFit = st.ToFit, Stamps = st.Stamps, Planning = st.Planning, PuttingBack = st.PuttingBack, Pieces = st.Pieces,
                FittedPerSecond = st.FittedPerSecond, AddedPerSecond = st.AddedPerSecond, Fps = st.Fps, MsPerFrame = st.MsPerFrame,
                HistLine = NeighbourPolishSystem.HistLine.ToArray(), HistFitted = NeighbourPolishSystem.HistFitted.ToArray(),
                HistAdded = NeighbourPolishSystem.HistAdded.ToArray(), HistFps = NeighbourPolishSystem.HistFps.ToArray(),
            };
            foreach (var kv in st.Zones) v.Zones.Add((kv.Key, kv.Value.Total, kv.Value.Polished));
            foreach (var kv in st.Looks.OrderByDescending(k => k.Value)) v.Looks.Add((kv.Key, kv.Value));
            v.Details.Add(("plans", NeighbourPolishSystem.PlansTimed));
            v.Details.Add(("planAvgMs", NeighbourPolishSystem.PlansTimed > 0 ? NeighbourPolishSystem.PlanWorkerMs / NeighbourPolishSystem.PlansTimed : 0));
            v.Details.Add(("planWorstMs", NeighbourPolishSystem.PlanWorkerMaxMs));
            v.Details.Add(("frameWorstMs", NeighbourPolishSystem.UpdateMaxMs));
            v.Details.Add(("taken", NeighbourPolishSystem.TotalTaken));
            v.Details.Add(("newFlagged", NeighbourPolishSystem.TotalFlaggedNew));
            v.Details.Add(("fencesMoved", NeighbourPolishSystem.TotalMoved));
            v.Details.Add(("objectsMade", NeighbourObjectSystem.TotalMade));
            v.Details.Add(("surfacesMade", NeighbourAreaSystem.TotalMade));
            v.Details.Add(("lawnsWritten", NeighbourLawnSystem.TotalWritten));
            v.Details.Add(("paintStamps", NeighbourPaintSystem.TotalStamps));
            v.Details.Add(("lookAreas", NeighbourPolishSystem.LookAreaCount));
            return v;
        }

        private PainterView PainterState()
        {
            var (lots, yes) = PainterCount;
            var s = global::Placemaker.Game.Tool.ToolRegistration.Settings;
            var v = new PainterView
            {
                Lots = lots, Polished = yes, FeatureOn = NeighbourPolishSystem.FeatureEnabled && !NeighbourPolishSystem.Paused, NewHouses = NeighbourPolishSystem.PolishNew, LevelUps = NeighbourPolishSystem.PolishLevelUps,
                GameChanges = NeighbourPolishSystem.PolishGameChanges, GameRemovals = NeighbourPolishSystem.PolishGameRemovals, Theme = PainterLook,
                Tool = _painter,
                CityPolished = NeighbourPolishSystem.StatusPolished, CityTotal = NeighbourPolishSystem.StatusTotal,
                Waiting = NeighbourPolishSystem.StatusWaiting, Restoring = NeighbourPolishSystem.StatusRestoring,
            };
            var current = CurrentLook;
            v.LookTheme = _lookTheme;
            v.Live = _polishLive;
            v.Modified = LookChanges() != null;
            v.Values = new Dictionary<string, float>(_lookValues);
            v.Preview = _gardenPicture;
            v.LookValues = current != null ? current.Values() : new Dictionary<string, float>(PolishLooks.Defaults);
            v.LookSavedTheme = current?.Theme ?? "garden";
            foreach (var l in NeighbourPolishSystem.Looks) v.Looks.Add((l.Id, l.Name, l.Custom, l.Theme, l.Edited, l.Knobs, PolishLooks.ColorOf(l)));
            v.Swatches = PolishLooks.Swatches.ToArray();
            v.Defaults = new Dictionary<string, float>(PolishLooks.Defaults);
            v.TypeLook = _recipe != null ? Placemaker.Core.Layers.Types.Zoning.PolishLookLayer.Of(_recipe.Root.Layer)?.LookId : null;
            v.Lens = OutgrowLens;
            v.LensWanted = _lensWanted;
            v.LensCounts = (int[])NeighbourPolishSystem.LensCounts.Clone();
            v.LensMode = _lensMode;
            foreach (var kv in NeighbourPolishSystem.LensTypeCounts.OrderByDescending(k => k.Value)) v.LensTypes.Add((kv.Key, kv.Value));
            v.Brush = _brush > 0 ? "paint" : _brush < 0 ? "erase" : "";
            v.BrushLook = _brushLook ?? "";
            v.BrushSize = _brushWidth;
            v.OutgrowMode = _outgrowMode;
            if (NeighbourPolishSystem.StatusInCity) v.Stats = StatsView();
            v.AreaZones.AddRange(_areaZones);
            var polish = World.GetExistingSystemManaged<NeighbourPolishSystem>();
            if (polish != null)
                foreach (var (zone, kind, icon) in polish.Zones())
                    v.Zones.Add((zone, kind, NeighbourPolishSystem.ZoneLook(zone, kind), NeighbourPolishSystem.ZoneHasOwnLook(zone), icon, NeighbourPolishSystem.DefaultZoneLook(zone, kind)));
            if (s != null)
            {
                v.Painted = s.PolishPaintedGardens;
                v.Lawns = s.PolishLawns; v.Planting = s.PolishPlanting; v.Features = s.PolishFeatures; v.Paths = s.PolishPaths;
                v.Rocks = s.PolishRocks; v.GroundPaint = s.PolishGroundPaint; v.ShopPaving = s.PolishPavedFill;
                v.Homes = s.PolishThemeHomes; v.Shops = s.PolishThemeShops; v.Offices = s.PolishThemeOffices; v.Industry = s.PolishThemeIndustry;
            }
            return v;
        }
    }
}
