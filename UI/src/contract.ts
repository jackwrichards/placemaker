// UI ↔ C# binding contract. Authoritative: C# (Game/UI/PlacemakerUISystem.cs + UiModels.cs)
// mirrors these shapes exactly. Group: "Placemaker". All JSON values are sent via
// RawValueBinding / ValueBinding<string> (never string[] — that broke mod load in PLT).
//
// Few, structured bindings instead of hundreds: the UI is schema-driven, so a new layer type
// needs zero UI code.

export const GROUP = "Placemaker";

/**
 * (s15) UI ↔ DLL contract version; C# sends it as state.protocol (Core/Tool/UiModels.Protocol). Bump both
 * together. The game reloads this UI file from disk but loads the DLL only at start, so right after an
 * update they can disagree: the panel then shows "Restart the game" (absent = a DLL older than s15).
 */
export const PROTOCOL = 25;

// ─────────────────────────── Values (C# → UI) ───────────────────────────

/** Workflow stages. The player can always click back to an earlier stage; nothing is lost. */
export type Stage = "area" | "design" | "review" | "building" | "manage";
/** "blockPaint" (s11): a brush fenced to the city block under the cursor; uses the same brush settings as "paint".
 *  (PROTOCOL 24, area overhaul) "perimeter" (Road nodes) is folded into "draw": Points snap to road nodes. The DLL still
 *  answers "perimeter" with Points; the panel no longer offers it. */
export type AreaMode = "draw" | "block" | "paint" | "perimeter" | "blockPaint";
export type BrushShape = "round" | "square";

/** Block paint brush rules (s11; plain Paint ignores them). Persisted in the mod settings. */
export interface BrushRules {
  roads: boolean;        // stop at the curb, keep the stroke's side of a road
  buildings: boolean;
  lots: boolean;         // parks, other Placemaker areas, other lots
  zoned: boolean;        // zoned cells with a building on them (s49); vacant zoned land is paintable
  /** (s49) also keep off zoned cells nobody built on yet (default off) */
  allZoned?: boolean;
  emptyZones: boolean;   // the empty zone grid along roads
  water: boolean;
  stayInBlock: boolean;  // lock a stroke to the closed block it starts in
  /** (s27) other Placemaker builds (owner lot + zone blocks), never the group being edited */
  builds?: boolean;
  /** (s27) "Paint over anything": every stop and the block lock off */
  anything?: boolean;
  /** (s27) Draw / Roads / Block outlines are clipped to free land (the polygon stops) */
  drawClip?: boolean;
  snap: number;          // m, 0 = off (default 6)
  smoothing: number;     // 0–6 passes (default 1; Paint uses 6)
  /** (s43, PROTOCOL 22) What the Paint bucket treats as a wall; setBrushRules({ walls: { paths: false } }) merges per key.
   *  (PROTOCOL 24) No longer read: every tool uses the stops above. */
  walls?: BucketWalls;
  /** (PROTOCOL 24, area overhaul) One set of stops for Points, Paint bucket and Paint area: footpaths, bike paths, tracks. */
  paths?: boolean;
  bikes?: boolean;
  tracks?: boolean;
  /** (PROTOCOL 24) Zoning as one choice: 0 off, 1 built on, 2 zoned (built or not, default), 3 the whole grid. Sets zoned/allZoned/emptyZones. */
  zoning?: number;
  /** (PROTOCOL 24) Openings narrower than this (m) count as closed; narrower places are left out (0 = off; default 10). */
  gap?: number;
  /** (PROTOCOL 24) How far (m) the Paint bucket spreads from the click (default 500). */
  reach?: number;
}

/** (s43) Paint bucket walls: lines (roads, paths, bikes, tracks) trace the block, areas (water, lots, zoned) cut it. */
export interface BucketWalls {
  roads: boolean;   // every car road: alleys, gravel, one-way, pedestrian streets (default on)
  paths: boolean;   // pedestrian paths (default on)
  bikes: boolean;   // bicycle-only paths (default on)
  tracks: boolean;  // train / tram tracks at ground level (default on)
  water: boolean;   // water edge (default on)
  lots: boolean;    // buildings, other lots, other Placemaker builds (default off)
  zoned: boolean;   // zoned cells (default off)
}

export interface LocText { key: string; args?: (string | number)[] }

export interface PrimaryAction {
  /** What pressing the main button (or Enter) does now. */
  action: "none" | "toDesign" | "toReview" | "build" | "placeMarkers" | "cancelBuild" | "finishEdit" | "polish";
  label: LocText;
  enabled: boolean;
  /** Why disabled, shown as tooltip and inline hint. */
  reason?: LocText;
  /** Additive (FEEDBACK-4b), action "placeMarkers" only: the step that still needs entrances and the kind to
   *  place. Disabled ("Add at least one entrance"), yet pressing it starts placement there (C# Primary). */
  stepPath?: string;
  markerKind?: string;
}

export interface AreaInfo {
  valid: boolean;
  closed: boolean;
  pointCount: number;
  areaM2: number;
  perimeterM: number;
  /** Problem with the current shape, e.g. self-intersection or too small for the recipe. */
  problem?: LocText;
}

export interface Hint { text: LocText; keys?: string[] }   // e.g. keys ["Ctrl","Z"]

/** state — changes often (every interaction). */
export interface ToolState {
  open: boolean;           // tool active
  stage: Stage;
  /** Stages the player may jump to right now (others are greyed). */
  reachable: Stage[];
  areaMode: AreaMode;
  /** rules: sent by s11+ C# (absent = older DLL). */
  brush: { shape: BrushShape; size: number; angle: number; rules?: BrushRules };
  area: AreaInfo;
  primary: PrimaryAction;
  canUndo: boolean;
  canRedo: boolean;
  undoLabel?: LocText;
  redoLabel?: LocText;
  /** Marker placement mode active (entrances/access points/split line) and for which step path. */
  markerMode?: { stepPath: string; kind: string; label: LocText; /** The entrances placed next come with a crosswalk (walking kinds). */ crosswalk?: boolean };
  hints: Hint[];
  /** Non-null while the composer is planning in the background. */
  planning: boolean;
  /** Build progress 0..1 while stage === "building". */
  buildProgress?: number;
  /** When editing an existing build, its group id. */
  editingGroup?: number;
  /** Anarchy mod detected → show its toggle. */
  anarchy?: { available: boolean; enabled: boolean };
  /** The build becomes one group (not a loose road grid). separate = the Review switch "One group" is off: built in plain pieces (dissolved right after the build). */
  grouping?: { separate: boolean };
  /** The entrances placed next come with a crosswalk across the street (walking entrance kinds only). */
  entranceCrosswalk?: boolean;
  /** Review's "Keep area" switch: Build keeps the area and the panel (true, default) or closes Placemaker. */
  keepArea?: boolean;
  panelCollapsed: boolean;
  /** "sketch" = fast overlay lines/dots; "ghost" = real translucent game models + surfaces (Review uses ghost by default). */
  previewMode?: "sketch" | "ghost";
  /** True while the ghost is being (re)built after a change. */
  ghostPending?: boolean;
  /** (s15) Contract version of the DLL (see PROTOCOL); absent = older than s15. */
  protocol?: number;
  /** (s15) Every selection mode the DLL handles, and whether it can be used now (absent = older DLL). */
  areaModes?: { mode: AreaMode; enabled: boolean; reason?: LocText }[];
  /** Rough planning progress 0..1 while planning (updated ~10 Hz). */
  planningProgress?: number;
  /** Additive (s22): preview layers the player showed / hid with the legend (layer id → visible). Absent = every
   *  layer at its default (LegendEntry.defaultOff). Kept for the game session. */
  legendVisibility?: Record<string, boolean>;
  /** (PROTOCOL 24, area overhaul) The legend while choosing the area: what the overlay shows for the current tool
   *  (ids "area:*": outline, fill, doors = closed gaps, reach, stops, painted, fence, leftOut, node). Area stage only. */
  areaLegend?: LegendEntry[];
  /** (PROTOCOL 25) The neighbourhood-polish painter ("Polish gardens") is the pick: the area is stamped (primary "polish"
   *  = Polish, trigger polishStamp(false) = Unpolish), never built. Absent = not the painter. */
  painter?: PainterState;
}

/** (PROTOCOL 25) The painter as the panel shows it. */
export interface PainterState {
  /** Lots whose middle lies in the area, and how many of them are polished now. */
  lots: number;
  polished: number;
  /** This city's master switch "Outgrow" (off: paused; the flags are kept, gardens change once it is on). */
  featureOn: boolean;
  /** "Outgrow new buildings", this city's. */
  newHouses: boolean;
  /** "Outgrow again when a building levels up", this city's (off: a level-up keeps its garden; nothing is planned). */
  levelUps: boolean;
  /** "Outgrow again when the game changes a building" (abandoned, condemned: off, its fences go back where they were) and
   * "... removes a building nearby" (off: the gardens around keep theirs), this city's. */
  gameChanges: boolean;
  gameRemovals: boolean;
  /** The theme Polish gives: "zone" (each house by its zone's setting), "garden", "natural", "tidy", "yard". */
  theme: string;
  /** The whole city: each kind's look for new buildings, how many are polished, work left. */
  city?: PolishCity;
  /** The look picked is `theme` ("zone": each building its zone's look). Its base theme as the rows have it, as saved. */
  lookTheme: string;
  lookSavedTheme: string;
  /** The rows changed the look (not saved). */
  modified: boolean;
  /** The old polish painter tool; false: anything else in Placemaker's panel (the Global outgrow window is always there). */
  tool?: boolean;
  /** Live polish: the selection polished again a moment after every change (the switch beside Polish). */
  live?: boolean;
  /** Every setting of the look as the rows have it, and as the look has it (reset). */
  values?: Record<string, number>;
  lookValues?: Record<string, number>;
  /** Every look: the built-in ones (name is a key; edited: the player changed it) and the player's own, each with the
   *  settings that differ from `defaults` (a look's values: defaults, then knobs). */
  looks?: { id: string; name: string; custom: boolean; theme: string; edited?: boolean; knobs?: Record<string, number>; color?: string }[];
  defaults?: Record<string, number>;
  /** The colours an outgrow type can be given. */
  swatches?: string[];
  /** Global outgrow's numbers (absent: no city). */
  stats?: PainterStats | null;
  /** The status lens: drawn now (window open, switch on), its switch, its map ("status" | "types"), what it shows: by layer
   *  (outgrown, not, being fitted, just added, waiting < 10 s, < 30 s, < 90 s, longer) or by outgrow type ("-": not outgrown). */
  lens?: boolean;
  lensWanted?: boolean;
  lensMode?: string;
  lensCounts?: number[];
  lensTypes?: { id: string; count: number }[];
  /** Global outgrow's brush: "" off, "paint" (brushLook) or "erase". */
  brush?: string;
  brushLook?: string;
  /** The brush's diameter in metres. */
  brushSize?: number;
  /** The panel shows Outgrow (its switcher; the toolbar's Outgrow button) instead of Placemaker's stages. */
  outgrowMode?: boolean;
  /** A Zoning type picked: the look its zones take unless one picks its own. */
  typeLook?: string;
  /** Every zone type of the game: its look ("off": not polished) and whether it has its own (else its kind's). */
  zones?: PolishZone[];
  /** The zone types of the buildings in the selection: key (the zone's name without EU / NA, lower case), a zone prefab of
   *  it, its icon, how many buildings (a Zoning type's Only edit existing). */
  areaZones?: { key: string; zone: string; icon: string; count: number }[];
  /** The editor's garden picture: a small made-up street planned with the rows' values (x, z: z north; rings of points). */
  preview?: { lo: number[]; hi: number[]; roads: number[][][]; lots: number[][][]; houses: number[][][]; gardens: number[][][]; limits?: number[][][]; ms: number } | null;
}

export interface PolishZone {
  id: string;
  kind: "home" | "shop" | "office" | "industry";
  look: string;
  own: boolean;
  /** The game's own zone icon (UIObject.m_Icon, "Media/…"), or "". */
  icon: string;
  /** The zone type's own built-in look (what it gets with no pick of its own nor of its kind). */
  def?: string;
}

/** Global outgrow's numbers (NeighbourPolishSystem.Stats), with their last two minutes (one sample a second). */
export interface PainterStats {
  /** By kind: homes, shops, offices, industry. */
  kindTotal: number[];
  kindPolished: number[];
  /** Zone prefab name → [buildings, outgrown]. */
  zones: Record<string, [number, number]>;
  /** Outgrown buildings by outgrow type id ("" none). */
  looks: Record<string, number>;
  toFit: number;
  stamps: number;
  planning: number;
  puttingBack: number;
  pieces: number;
  fittedPerSecond: number;
  addedPerSecond: number;
  fps: number;
  msPerFrame: number;
  histLine: number[];
  histFitted: number[];
  histAdded: number[];
  histFps: number[];
  details: Record<string, number>;
}

export interface PolishLook {
  /** Painted gardens instead of lawns: share 0–100 (Natural and Yard more, Tidy fewer). */
  painted: number;
  lawns: boolean;
  planting: boolean;
  features: boolean;
  paths: boolean;
  rocks: boolean;
  groundPaint: boolean;
  shopPaving: boolean;
}

export interface PolishCity {
  /** Each kind's look for new buildings: "auto" (each zone type its own look), a look id, or "off". */
  homes: string;
  shops: string;
  offices: string;
  industry: string;
  polished: number;
  total: number;
  /** Buildings waiting to be fitted, gardens being put back (a few a frame). */
  waiting: number;
  restoring: number;
}

/** One control, mirrors Core.ControlSchema. */
export interface Control {
  id: string;
  kind: "slider" | "toggle" | "choice" | "asset" | "assetMulti" | "markers" | "arrangement";
  tier: "essential" | "more";
  group: string;
  label: string;            // localisation key
  tooltip?: string;         // localisation key
  /** Additive (s16): one visible line under the label saying what the control is FOR (localisation key). */
  description?: string;
  icon?: string;
  min?: number; max?: number; step?: number; unit?: string;
  options?: ControlOptionView[];
  assetCategory?: string;
  markerKinds?: string[];
  /** Applies only when "otherId=value" (s15: shown greyed out with a reason while it does not, unless hideWhenOff). */
  visibleWhen?: string;
  /** (s15) Hide instead of greying out while visibleWhen is false (the documented exceptions). */
  hideWhenOff?: boolean;
  /** (s15) Enabled only when (visibleWhen syntax); otherwise shown greyed out with disabledReason. */
  enabledWhen?: string;
  /** (s15) Localisation key of the greyed-out reason; absent = worded from the condition. */
  disabledReason?: string;
  cheap?: boolean;
  /** Default value (for per-control reset). */
  default?: SettingValue;
}

/** One option of a choice control. */
export interface ControlOptionView {
  value: string;
  label: string;            // localisation key
  icon?: string;
  tooltip?: string;
  /** Additive (s16): a visible line under the option label, e.g. "Streets: Two-Lane · Avenues: Four-Lane" (localisation key). */
  detail?: string;
  /** Additive (s16): a tiny generated picture of the option — stripes by role in their colours and real widths (road mixes). */
  swatch?: SwatchStripe[];
}

/** One stripe of an option swatch (C# Core.OptionSwatch). */
export interface SwatchStripe {
  role: string;             // roads: "street" | "avenue" | "local" | "roundabout" | "perimeter" | "carFree"; (s18) ground palettes: "field" | "border" | "focal" | "path" | "inlay"
  color: string;            // "#rrggbb"
  width: number;            // real metres; draw relative to the other stripes
  green?: boolean;          // trees / grass verges
  bike?: boolean;           // bike lanes
}

/** Additive (s16): what an asset control really places in the latest plan ("Automatic · Two-Lane Road"). */
export interface ResolvedAsset {
  id: string;               // prefab name
  name?: string;            // localised display name (absent until the game catalog knows it)
  icon?: string;            // thumbnail url
  /** The current layout does not use this control: id is what Automatic WOULD place (e.g. avenues in a layout without avenues). */
  unused?: boolean;
}

/** Additive (s16): the roads a step's plan places, by role — the map legend (same colours as the preview). */
export interface NetLegendEntry {
  role: string;             // "street" | "avenue" | "local" | "roundabout" | "perimeter" | "carFree"
  label: string;            // localisation key ("Placemaker.Roads.Role.street")
  color: string;            // "#rrggbb", identical to the in-game preview line colour
  count: number;            // segments
  lengthM: number;          // total length, metres
  assetId?: string;         // main prefab of the role
  assetName?: string;       // its localised display name
  control?: string;         // the asset control that picks this role's road
}

/**
 * Additive (s22): one layer of the map preview (C# Core/Build/PreviewLegend). Every element the overlay draws belongs
 * to exactly one layer. Ids: "paths", "road:<role>" (s16 roles), "features", "trees", "planting", "water",
 * "surfaces", "objects", "parking", "entrances", "terrain", "cutouts", "blocked", "decals".
 */
export interface LegendEntry {
  id: string;
  label: string;            // localisation key ("Placemaker.Legend.trees", roads "Placemaker.Roads.Role.street")
  color: string;            // "#rrggbb", the overlay colour of the layer
  /** Second colour (terrain: cut; color = fill). */
  color2?: string;
  count: number;            // elements (segments, objects, cells, heat-map pieces…)
  lengthM?: number;         // nets: total length
  areaM2?: number;          // surfaces, water, terrain change, cells
  /** Terrain: deepest cut / highest fill, metres. */
  minDelta?: number;
  maxDelta?: number;
  /** (s61) Issues: how many of the markers are red (left out / blocked); the rest are orange warnings. */
  blocked?: number;
  /** Hidden unless the player shows it (blocked cells, decals). */
  defaultOff?: boolean;
  /** "roads" for the road roles (shown as one group). */
  group?: string;
  assetId?: string;         // roads: main prefab of the role
  assetName?: string;       // its localised display name
  control?: string;         // roads: the asset control that picks it
}

export interface LayerType {
  id: string;
  name: string;             // localisation key
  icon: string;
  kind: "fill" | "split" | "addOn";
  accepts: string[];        // region tags it can run in ([] = any)
  emits: string[];          // region tags it produces
  controls: Control[];
  presets: PresetView[];
  /** Not offered as its own card in the Make row (split, pond): used by recipes / features only. */
  hidden?: boolean;
}

export interface RecipeInfo {
  id: string;
  name: string;             // localisation key, or literal when custom === true
  icon: string;
  custom: boolean;
  /** Short description key for the Make row tooltip. */
  description?: string;
  /** Make-row group declared by the type ("parks", "gardens", "squares", "sports", "water", "nature", "farms", "events", "industrial", "transport", "parking", "commercial", "roads"). */
  group?: string;
  /** (core) The Make-row category the central shelf puts it in (Recipes/shelf/<category>.json: "parks", "beach", "shops", …). */
  category?: string;
  /** (core) Hidden by the shelf (its category is hidden or the category hides it): not in the picker or its search. */
  shelved?: boolean;
  /** (PROTOCOL 25) The neighbourhood-polish painter ("polishGardens"): picked like a type, but it stamps lots, it builds nothing. */
  painter?: boolean;
}

/** (core) A Make-row category of the central shelf, in Make-row order. */
export interface CategoryInfo {
  id: string;
  /** Localisation key ("Placemaker.UI.MakeGroup.Parks"). */
  name: string;
  /** English label (used when the key has no translation). */
  en: string;
  order: number;
  /** The whole category is hidden (industry, transport). */
  hidden?: boolean;
}

/** catalog — changes rarely (load, mods/assets change). */
export interface Catalog {
  layers: LayerType[];
  recipes: RecipeInfo[];    // built-in "types" (park, plaza, parking, roads, roads+parks…) + player recipes
  /** (core) The central shelf's categories in Make-row order (absent: every type group is its own category). */
  categories?: CategoryInfo[];
}

export type SettingValue = number | boolean | string | string[];

/** A recipe step as the UI sees it (mirrors Core.RecipeStep, paths address steps: "0", "0/1", "0/1@block:3"). */
/** A built-in feature of a fill layer, shown as a toggle (mirrors Core.FeatureInfo). */
export interface FeatureView {
  layer: string;            // feature layer id
  label: string;            // localisation key
  icon: string;
  enabled: boolean;
  /** Path of the child step when enabled (its controls are shown under the toggle, collapsed by default). */
  path?: string;
  /** Tooltip key (explanation lives here, labels stay short). */
  tooltip?: string;
  /** Ids of the PARENT step's controls that belong to this feature (e.g. park plaza shape / position / size):
   *  render them under the toggle while it is on, and hide them from the parent's own groups. */
  options?: string[];
  /** On by default for new designs. */
  defaultOn?: boolean;
  /** Additive (FEEDBACK-4b #8): did the enabled feature fit in the latest plan? Absent = not known yet
   *  (no plan, feature off, or a stale plan that has not run it). */
  status?: FeatureFit;
  /** (s15) The feature cannot be used for this area, and why (toggle shown greyed out; turning it off still works). */
  disabled?: LocText;
}

/** Fit of an enabled feature in the latest plan (C# UiModels.FeatureFit). */
export interface FeatureFit {
  /** ok = placed as asked; reduced = placed smaller / fewer / only in some places; failed = nothing placed (no room). */
  kind: "ok" | "reduced" | "failed";
  /** Why: the feature layer's own issue, or Placemaker.UI.Feature.NoRoom / .SomeMissing. Absent for ok. */
  text?: LocText;
  /** Judged from the previous plan while a new one is pending. */
  stale?: boolean;
}

export interface StepView {
  /** Built-in features this (fill) step offers. */
  features?: FeatureView[];
  path: string;
  layer: string;
  target: string;           // "" = same region; tag; or region key
  enabled: boolean;
  values: Record<string, SettingValue>;
  locked: string[];         // ids set explicitly by the player
  markers: { kind: string; count: number }[];
  /** Every placed marker in placement order (index = removeMarker index). */
  markerList?: { kind: string; index: number; position: [number, number, number]; /** with a crosswalk across the street */ crosswalk?: boolean }[];
  /** Marker kinds this step accepts, in display order (e.g. entrance, entry, exit, alley, footpath). crosswalk = people walk in there: it can have one. */
  markerKinds?: { kind: string; label: string; icon?: string; tooltip?: string; crosswalk?: boolean }[];
  /** Minimum markers needed before Build (0 = optional). */
  markersRequired?: number;
  /** Readiness from IAreaGenerator.CheckReady for this step. */
  ready: boolean;
  nextStep?: LocText;
  children: StepView[];
  /** region key → override step */
  overrides: Record<string, StepView>;
  /** Regions this step produced in the latest plan (for "then fill blocks with…" and per-region overrides). */
  regions: { key: string; tags: string[]; areaM2: number }[];
  /** Additive (s16): asset control id → the prefab the latest plan uses for it (what "Automatic" became). */
  resolvedAssets?: Record<string, ResolvedAsset>;
  /** Additive (s16): the plan's roads by role (only roles present), for the legend. */
  netLegend?: NetLegendEntry[];
  /** (s15) Controls that do not apply to the current area: control id → why (shown greyed out). */
  disabled?: Record<string, LocText>;
}

/** design — the recipe being edited. */
/** A preset (subtype) of a type: built-in, a player's override of a built-in, or a player's own. */
export interface PresetView {
  id: string;
  /** Localisation key for built-ins; literal text when custom. */
  name: string;
  icon?: string;
  /** Player-made preset (★). */
  custom?: boolean;
  /** Built-in preset the player saved over ("Reset to default" available). */
  overridden?: boolean;
  /** For custom presets: the built-in it was derived from (display only). */
  basedOn?: string;
  /** (core) Hidden by the central shelf: not in the picker or its search (shown only while it is the active style). */
  shelved?: boolean;
}

/** The preset currently applied to the focused fill step. */
export interface ActivePreset {
  id: string;               // "" = none (manual settings)
  layer: string;
  /** Current settings/features differ from the preset as saved. */
  modified: boolean;
  /** Save (overwrite) allowed: custom presets and built-ins (creates an override). */
  canSave: boolean;
}

export interface DesignState {
  /** Active preset of the root fill step (drives Save / Save as / Reset in the browser). */
  activePreset?: ActivePreset;
  /** Absolute path of ModsData/Placemaker/presets.json (shown after export). */
  presetsFile?: string;
  recipeId: string;         // which Make entry is selected
  modified: boolean;        // differs from the recipe's shipped/saved version
  root: StepView | null;
  /** Step currently focused in the panel (its controls are shown). */
  focus: string;
}

export interface Stat { key: string; value: number; unit: string }
/** (s61) The setting behind an issue: control id, its label key and its value as shown ("120 m", "On"). */
export interface IssueCause { id: string; label: string; value: string }
export interface Issue {
  severity: "info" | "warning" | "error";
  text: LocText;
  stepPath?: string;
  at?: [number, number, number];
  /** (s61) The setting that caused it (named in the chip, one click to change it). */
  cause?: IssueCause;
  /** (s61) Something was left out / blocked: red marker and chip (orange = a warning). */
  dropped?: boolean;
}

/** (s53) One reason a planned piece was not built (or was kept despite a collision). */
export interface AuditReason { id: string; label: string; arg: string; count: number; kept: boolean; top: string }
export interface BuildAudit { leftOut: number; kept: number; reasons: AuditReason[] }

/** summary — the review card. Always reflects the latest finished plan. */
export interface Summary {
  ready: boolean;           // a plan exists for the current area + design
  stale: boolean;           // inputs changed, new plan pending
  stats: Stat[];            // merged across layers ("trees", "stalls", "roadLength"…)
  cost: number;             // one-off construction cost (money)
  upkeep: number;           // per month (what the game charges: the roads and paths, and the park building)
  /** The park building's share of upkeep (vanilla-like, a park's running cost), when the build gets one. */
  parkUpkeep?: number;
  canAfford: boolean;
  /** Per-layer breakdown for the review stage. */
  layers: { stepPath: string; layer: string; regionKey: string; stats: Stat[]; cost: number; upkeep: number }[];
  issues: Issue[];
  /** Permanent effects the player must acknowledge (e.g. lake excavation). */
  permanent: LocText[];
  /** True once the player ticked the permanent-effects box for exactly these effects. */
  permanentAcknowledged?: boolean;
  /** Additive (s53): what the last build left out, by reason (C# Core/Build/BuildAudit.ToUi). Absent = nothing to report. */
  audit?: BuildAudit;
  /** (s33, PROTOCOL 21) False when the permanent effects are ground paint only: listed as one info line, no tick needed.
   *  Absent (older DLL) = every permanent effect needs the tick. */
  permanentNeedsAck?: boolean;

  /** Additive (s17, docs/PARK-STATS.md): park stats of the planned build. Absent = not a park type (parking,
   *  roads, industrial) or no plan yet. */
  parkStats?: ParkStatsView;
  /** Additive (s45, PROTOCOL 22): the cost by friendly category ("planting" | "paths" | "roads" | "parking" | "shops" |
   *  "water" | "play" | "ground" | "extras" | "other"), most expensive first; adds up to cost. Absent = older DLL. */
  costBuckets?: { id: string; cost: number; upkeep: number }[];
  /** Additive (s22): the preview layers of the plan, in display order (the legend window). Absent = no plan. */
  legend?: LegendEntry[];
}

/** Additive (sketch study, C# Core/Build/SitePlan.cs ToUi): the plan drawn as what gets built, for the panel's top view; the
 *  map's sketch draws the same list (setting Sketch style: Site plan). World metres, x east and z north; point lists are flat
 *  ([x, z, x, z, …]); discs are [x, z, r, …]; badges [x, z, angle, …]. Null = no plan. */
export interface SitePlanView {
  /** Changes with every new plan. */
  stamp: string;
  /** The map's sketch style: siteplan (the floating legend steps aside for the card's) or classic. */
  style?: "siteplan" | "classic";
  stale?: boolean;
  min: number[];
  max: number[];
  areaM2: number;
  /** A point well inside the outline (its label). */
  centre: number[];
  outline: number[];
  fills: { m: string; color: string; alpha: number; row: string; rings: number[][] }[];
  cars: SitePlanStroke[];
  walks: SitePlanStroke[];
  /** The city's own streets near the build (context). */
  streets: SitePlanStroke[];
  caps: number[];
  fences: number[][];
  /** [ax, az, bx, bz, …] painted stall lines. */
  stalls: number[];
  chargers: number[];
  /** Every object as a rough model at its footprint, grouped by legend row and colour (rings: footprints; discs: [x, z, r, …]). */
  props: { row: string; color: string; rings: number[][]; discs: number[] }[];
  /** The footprints of the tall objects, moved to where their shadow falls. */
  propShadows: number[][];
  trees: number[];
  bushes: number[];
  plots: { color: string; q: number[] }[];
  /** The ground change by band (red raised, blue lowered; Core TerrainHeat), the "terrain" row. Absent = none. */
  heat?: { band: number; color: string; alpha: number; rings: number[][] }[];
  entrances: number[];
  issues: number[];
  legend: SitePlanRow[];
}

export interface SitePlanStroke { w: number; p: number[]; mark?: boolean }

/** One legend row: what is drawn, in plain words; the id is the map layer it hovers / hides (state.legendVisibility). */
export interface SitePlanRow {
  id: string;
  /** Swatch shape: road, bays, path, grass, paving, water, zone, terrain, tree, bush, place, building, vehicle, storage, junk,
   *  crop, stone, barrier, furniture, entrance, issue. */
  sym: string;
  label: LocText;
  /** English fallback of the label. */
  en: string;
  count: number;
  /** "m" a length, "count", "cells", "delta" the biggest ground change in metres (±), "" no number. */
  unit: string;
  /** Where its note goes on the picture (inside the outline). */
  anchor: number[];
}

/** s17 park stat ids (save format, never renamed). */
export type ParkStatId = "attractiveness" | "leisure" | "coverageRange" | "coverageCapacity" | "entertainment" | "upkeep";

/** One stat the player can tune in Review (C# Core/Build/ParkStats.cs ParkStatEstimate). */
export interface ParkStatView {
  id: ParkStatId;
  /** What the build gets (override or suggested; clamped to [bandMin, bandMax] while keepVanilla). */
  value: number;
  /** Placemaker's estimate, already inside the vanilla range. */
  suggested: number;
  /** Range vanilla parks of a comparable size use, scaled to this size. */
  bandMin: number;
  bandMax: number;
  /** Hard limits (slider range while keepVanilla is off). */
  min: number;
  max: number;
  step: number;
  unit: "count" | "money" | "";
  /** The game uses this value for the build today (attractiveness → tourism). Others are shown and saved only. */
  applied: boolean;
  overridden: boolean;
  /** The set value was outside the vanilla range and got clamped. */
  clamped: boolean;
  /** How it is calculated, one line each (C# LocText, numbers in args). */
  formula: LocText[];
  /** Comparable vanilla parks: game name key ("Assets.NAME[id]") with an English fallback, lot area and their real value. */
  similar: { id: string; name: string; nameKey: string; areaM2: number; value: number }[];
}

export interface ParkStatsView {
  kind: "park" | "plaza" | "playground" | "sports" | "nature";
  /** Kind of leisure citizens get (vanilla LeisureType CityPark / CityBeach). */
  leisureType: "park" | "beach";
  /** "Keep within vanilla range" (default true). */
  keepVanilla: boolean;
  /** s17 companion park building: "yes" = every stat is real (hidden park building at the road edge), "noRoad" = the
   *  setting is on but no road frontage was found (only attractiveness applies), "off" = setting off (attractiveness only). */
  companion?: "yes" | "noRoad" | "off" | "ungrouped";
  /** Quality multiplier 0.55–2 (trees, water, landmarks, amenities, play, seating). */
  quality: number;
  stats: ParkStatView[];
}

export interface AssetItem {
  id: string;               // prefab name
  name: string;             // localised display name (already translated by C#)
  icon: string;             // thumbnail url or ""
  tags?: string[];
  cost?: number;
}

/** assets — category → items; filled on demand via requestAssets. */
export type AssetLists = Record<string, AssetItem[]>;

/** manage — a built group under the cursor/selected (stage "manage"), or null. */
export interface ManagedGroup {
  id: number;
  recipeName: LocText;
  customName?: string;
  stats: Stat[];
  upkeep: number;
  canEdit: boolean;
}

export const Values = {
  state: "state",           // ToolState
  catalog: "catalog",       // Catalog
  design: "design",         // DesignState
  summary: "summary",       // Summary
  assets: "assets",         // AssetLists
  managed: "managed",       // ManagedGroup | null
  ai: "ai",                 // AiView | null (null while the AI builder option is off; docs/AI-BUILDER.md)
  sitePlan: "sitePlan",     // SitePlanView | null (additive, sketch study)
} as const;

// ─────────────────────────── AI builder (docs/AI-BUILDER.md) ───────────────────────────

/** One chat line: the player's or the AI's words as written, or a step line as a localisation key. */
export interface AiLine {
  who: "you" | "ai" | "step" | "error" | "tool" | "phase" | "vision";
  text?: string;                   // you, ai, error: as written; vision: name, story, signature moment, ideas (one per line)
  key?: string;                    // tool, phase, step: a localisation key
  args?: (string | number)[];
  round?: number;                  // tool: the AI's step it belongs to (1 = its first reply)
  tool?: string;                   // tool: the tool's name (picks the icon)
  problems?: number;               // tool: problems the action caused
}

export interface AiView {
  status: "idle" | "working" | "waiting" | "done" | "failed" | "stopped";
  busy: boolean;
  step: string;                    // localisation key of what it is doing now ("" = nothing)
  stepArgs: (string | number)[];
  cost: number;                    // USD so far in this session
  tokens: number;
  pieces: number;                  // what it has placed
  phase: "" | "read" | "build" | "review" | "detail" | "fix" | "done";
  round: number;                   // the AI's steps so far in this request
  startedAt: number;               // when this request started (ms since 1970, UTC)
  model: string;                   // e.g. anthropic/claude-sonnet-5.5
  thinking: string;                // low | medium | high
  mods: boolean;                   // modded assets allowed
  modsLocked: boolean;             // the session has started: the mods switch is fixed
  lines: AiLine[];
}

// ─────────────────────────── Triggers (UI → C#) ───────────────────────────
// Arguments are primitives; structured args are JSON strings.

export const Triggers = {
  toggleTool: "toggleTool",                 // ()
  togglePolish: "togglePolish",             // () — the neighbourhood-polish tool's own button (state.painter while it is open)
  close: "close",                           // ()
  setPanelCollapsed: "setPanelCollapsed",   // (collapsed: boolean)

  goToStage: "goToStage",                   // (stage: Stage) — only if in state.reachable
  primary: "primary",                       // () — same as Enter (Review: Build & keep area)
  buildAndClose: "buildAndClose",           // () — Review's second button: build, then close Placemaker
  polishTheme: "polishTheme",               // (theme: string) — the theme the painter's Polish gives
  polishOption: "polishOption",             // (id: string, value: number) — the painter card's look rows (toggles 1/0, painted 0–100)
  polishCity: "polishCity",                 // (what: string, value: string) — on / newHouses / levelUps / gameChanges / gameRemovals / homes|shops|offices|industry / polishAll / unpolishAll
  polishKnob: "polishKnob",                 // (id: string, value: number) — a setting of the look picked ("theme": base theme index)
  polishLook: "polishLook",                 // (what: string, arg: string) — save / saveAs / rename / delete / revert the look picked
  polishLive: "polishLive",                 // (on: boolean) — the Live switch beside Polish
  polishZone: "polishZone",                 // (zone: string, look: string) — a zone type's look ("off"; "" = its kind's)
  polishStamp: "polishStamp",               // (yes: boolean) — (PROTOCOL 25) the painter: Polish (true, = primary) / Unpolish (false) the lots in the area
  undo: "undo",                             // ()
  redo: "redo",                             // ()

  setAreaMode: "setAreaMode",               // (mode: AreaMode)
  setBrush: "setBrush",                     // (shape: BrushShape, size: number, angle: number)
  setBrushRules: "setBrushRules",           // (json: Partial<BrushRules> as a JSON string) — s11
  resetBrushRules: "resetBrushRules",       // () — s11
  clearArea: "clearArea",                   // ()

  selectRecipe: "selectRecipe",             // (recipeId: string)
  focusStep: "focusStep",                   // (path: string)
  addLayer: "addLayer",                     // (parentPath: string, layerId: string, target: string)
  removeStep: "removeStep",                 // (path: string)
  moveStep: "moveStep",                     // (path: string, delta: number)
  setStepEnabled: "setStepEnabled",         // (path: string, enabled: boolean)
  setStepTarget: "setStepTarget",           // (path: string, target: string)
  overrideRegion: "overrideRegion",         // (parentPath: string, regionKey: string, layerId: string | "")  "" removes override

  setValue: "setValue",                     // (path: string, controlId: string, valueJson: string) — commit (undoable)
  previewValue: "previewValue",             // (path: string, controlId: string, valueJson: string) — live while dragging
  endPreview: "endPreview",                 // ()
  resetStep: "resetStep",                   // (path: string)
  applyPreset: "applyPreset",               // (path: string, presetId: string)
  randomize: "randomize",                   // (path: string) — "" = whole recipe; respects locked values
  unlockValue: "unlockValue",               // (path: string, controlId: string)
  startMarkers: "startMarkers",             // (path: string, kind: string) — enter marker placement
  clearMarkers: "clearMarkers",             // (path: string, kind: string)

  saveRecipeAs: "saveRecipeAs",             // (name: string) — player recipe from current design
  deleteRecipe: "deleteRecipe",             // (recipeId: string)
  saveAsDefault: "saveAsDefault",           // (path: string) — this layer's values become defaults

  requestAssets: "requestAssets",           // (category: string)

  manageGroup: "manageGroup",               // (groupId: number)
  editGroup: "editGroup",                   // (groupId: number)
  removeGroup: "removeGroup",               // (groupId: number)
  renameGroup: "renameGroup",               // (groupId: number, name: string)
  setAnarchy: "setAnarchy",                 // (enabled: boolean)
  locate: "locate",                         // (x: number, y: number, z: number) — move camera to an issue
  acknowledgePermanent: "acknowledgePermanent", // (ack: boolean) — required before Build when summary.permanent is non-empty (Enter included)
  setMarkerKind: "setMarkerKind",           // (path: string, kind: string) — which marker type the next click places (entrance types etc.)
  removeMarker: "removeMarker",             // (path: string, index: number)
  setMarkerCrosswalk: "setMarkerCrosswalk", // (path: string, index: number, on: boolean) — index -1: the entrances placed next
  ungroupGroup: "ungroupGroup",             // (groupId: number) — dissolves the build for good
  setBuildUngrouped: "setBuildUngrouped",   // (on: boolean) — the next build is dissolved into plain pieces right after it is built
  setKeepArea: "setKeepArea",               // (on: boolean) — Build keeps the area (true) or closes Placemaker (false)
  setFeature: "setFeature",                 // (path: string, featureLayerId: string, enabled: boolean)
  setPreviewMode: "setPreviewMode",         // (mode: "sketch" | "ghost")
  selectPreset: "selectPreset",             // (layerId: string, presetId: string) — select type (single-layer recipe) + apply preset; undoable
  savePreset: "savePreset",                 // (path: string, presetId: string) — overwrite custom preset, or override a built-in, with the step's current settings+features
  savePresetAs: "savePresetAs",             // (path: string, name: string) — new custom preset from the step's current settings+features; becomes active
  renamePreset: "renamePreset",             // (layerId: string, presetId: string, name: string) — custom only
  deletePreset: "deletePreset",             // (layerId: string, presetId: string) — custom: delete; overridden built-in: reset to default
  aiSend: "aiSend",                         // (text: string) — the AI category: the player's message (starts the session for the selection, or continues it)
  aiStop: "aiStop",                         // () — stop the AI's work
  aiReset: "aiReset",                       // () — forget the conversation and what the AI placed
  aiMods: "aiMods",                         // (on: boolean) — allow modded assets; only before the first message
  exportPresets: "exportPresets",           // () — (re)write presets.json, sets design.presetsFile
  // s17 park stats (Review). Per build: kept with the recipe (undoable) and the build's receipt; never re-plans.
  setParkStat: "setParkStat",               // (id: ParkStatId, valueJson: string) — override one stat
  resetParkStat: "resetParkStat",           // (id: ParkStatId | "") — back to suggested; "" = all
  setParkStatsKeepVanilla: "setParkStatsKeepVanilla", // (keep: boolean) — clamp to the vanilla range
  // s22 preview legend. Hover isolates one layer on the map; show / hide persists for the game session.
  hoverLegend: "hoverLegend",               // (layerId: string) — "" = none
  setLegendVisible: "setLegendVisible",     // (layerId: string, visible: boolean)
  resetLegend: "resetLegend",               // () — every layer back to its default
  // s53 build audit: hover a "Left out" reason to mark where those pieces would have been ("" = none).
  hoverAudit: "hoverAudit",                 // (reasonId: string)
} as const;

// ─────────────────────────── Localisation ───────────────────────────
// All keys are registered with the game's LocalizationManager by C# (Lang/<locale>.json,
// late community locales supported). UI uses translate(key, fallback). Key prefix: "Placemaker.".
