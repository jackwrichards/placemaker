// Mock of Game/UI/PlacemakerUISystem + the tool's stage machine, faithful enough to click through the
// whole flow in a browser: area → design → review → build, roll back to any reachable stage, one undo
// stack for shape + design edits, previews while dragging, markers, per-region overrides, presets,
// randomize (respecting locks), player recipes, built groups with manage/edit/remove.
// It publishes exactly the six values of contract.ts and handles exactly its triggers.
import { defaultVisible, fakeLegend } from "./legend";
import {
    ActivePreset, AreaMode, BrushShape, Catalog, CategoryInfo, DesignState, FeatureFit, GROUP, Hint, LocText, ManagedGroup, PresetView, PrimaryAction, PROTOCOL, RecipeInfo, SettingValue, Stage, StepView,
    Issue, Summary, ToolState,
} from "../../src/contract";
import { publish, setTriggerHandler } from "../cs2/api";

// Area overhaul (PROTOCOL 24): one set of stops for every tool, zoning as one choice (2 = zoned, built on or not), gap and reach.
const DEFAULT_BRUSH_RULES = { builds: true, anything: false, drawClip: false, roads: true, buildings: true, lots: true, zoned: true, allZoned: true, emptyZones: false, water: true, stayInBlock: true, snap: 6, smoothing: 1,
    paths: true, bikes: true, tracks: true, zoning: 2, gap: 10, reach: 500,
    walls: { roads: true, paths: true, bikes: true, tracks: true, water: true, lots: false, zoned: false } };
import { addStrings } from "../cs2/l10n";
import { BLOCKS, blocksRing, Block, cellsOutline, NODES, paintCells } from "./city";
import { FAKE_FEATURES, FAKE_LAYERS, FAKE_RECIPES, FAKE_STRINGS, fakeAssets, FeatureDef, MockLayer, MockRecipe, MORE_TYPES, RecipeTemplate } from "./fakeCatalog";
import { composeFake, FAKE_LAYER_IDS } from "./fakePlanner";
import { defaultSurface, surfaceColor } from "./fakeAssets";
import { area, closestOnRing, dist, perimeter, selfIntersects, simplify, V2 } from "./geometry";
import { ComposeResult, StepModel } from "./types";
import { SHELF_CATEGORIES, shelve, styleShelved } from "./shelf";

/** s33: like Core UiModels.NeedsAck — ground paint alone needs no tick. */
function needsAck(permanent: { key: string }[]): boolean {
    return permanent.some((p) => p.key !== "Placemaker.Permanent.Paint");
}

const EN: Record<string, string> = { ...FAKE_STRINGS };
const en = (key: string, fallback?: string) => EN[key] ?? fallback ?? key.split(".").pop() ?? key;

/** (C# PresetStore.StandardId / StandardNameKey) Every fill type's own design, a style like the others. */
const STANDARD_ID = "standard";
const STANDARD_KEY = "Placemaker.UI.Preset.Standard";

interface Snapshot {
    ring: V2[];
    closed: boolean;
    cells: string[];
    picked: number[];
    recipeId: string;
    root: StepModel | null;
    focus: string;
    modified: boolean;
    active: ActiveModel | null;
}

/** What a preset stores: the step's values and its enabled features with their values (undefined = keep defaults). */
export interface PresetData {
    values: Record<string, SettingValue>;
    features?: Record<string, Record<string, SettingValue>>;
}

/** The preset applied to the root step and the state it had right after applying/saving (modified = differs). */
interface ActiveModel { id: string; layer: string; base: PresetData }

interface CustomPreset { id: string; name: string; basedOn?: string; data: PresetData }

/** JSON with sorted keys, for order-independent comparison. */
function canon(v: unknown): string {
    if (Array.isArray(v)) return "[" + v.map(canon).join(",") + "]";
    if (v && typeof v === "object") return "{" + Object.keys(v as object).sort().map((k) => JSON.stringify(k) + ":" + canon((v as any)[k])).join(",") + "}";
    return JSON.stringify(v);
}

export const PRESETS_FILE = "C:\\Users\\Player\\AppData\\LocalLow\\Colossal Order\\Cities Skylines II\\ModsData\\Placemaker\\presets.json";

interface Entry {
    label: LocText;
    before: Snapshot;
    after?: Snapshot;
    coalesce?: string;
    at: number;
}

export interface BuiltGroup {
    id: number;
    ring: V2[];
    root: StepModel;
    recipeId: string;
    recipeName: LocText;
    customName: string;
    plan: ComposeResult;
    stats: { key: string; value: number; unit: string }[];
    upkeep: number;
}

type Listener = () => void;

const clone = <T,>(v: T): T => JSON.parse(JSON.stringify(v));

export class MockBackend {
    // ── tool ──
    open = true;
    stage: Stage = "area";
    mode: AreaMode = "draw";
    brush = { shape: "round" as BrushShape, size: 40, angle: 0 };
    // Block paint brush rules (C# defaults); the mock has no obstacles, they are only shown and toggled.
    brushRules = { ...DEFAULT_BRUSH_RULES };
    /** (s15) Contract version the fake DLL reports; tests set an older one (or undefined) to see the restart notice. */
    protocol: number | undefined = PROTOCOL;
    /** (s15) Selection modes the fake DLL handles (undefined = not sent, like a DLL older than s15). */
    areaModes: ToolState["areaModes"] = (["draw", "block", "blockPaint", "paint", "perimeter"] as AreaMode[]).map((mode) => ({ mode, enabled: true }));
    userCollapsed: boolean | null = null;
    anarchy = { available: true, enabled: false };
    /** Entrances placed next come with a crosswalk (C# PlacemakerToolSystem._markerCrosswalk). */
    entranceCrosswalk = false;
    /** Review's "One group" switch off: the next build is born ungrouped. */
    buildSeparate = false;
    /** Review's "Keep area" switch: Build keeps the area (C# PlacemakerToolSystem._keepArea). */
    keepArea = true;
    money = 250000;

    // ── area ──
    ring: V2[] = [];
    closed = false;
    cells = new Set<string>();
    picked: number[] = [];
    hoverBlock = -1;

    // ── design ──
    layers: MockLayer[] = FAKE_LAYERS.map((l) => ({ ...l }));
    recipes: MockRecipe[] = FAKE_RECIPES.slice();
    recipeId = "park";
    root: StepModel | null = null;
    focus = "0";
    modified = false;
    /** Preset applied to the root step (null = none yet). */
    active: ActiveModel | null = null;
    /** Player presets per layer id (in-memory; C# keeps them in ModsData/Placemaker/presets.json). */
    customPresets: Record<string, CustomPreset[]> = {};
    /** Saved-over built-ins, "layer|presetId" -> data. */
    presetOverrides: Record<string, PresetData> = {};
    presetsFile: string | undefined;
    private presetSeq = 0;
    preview = new Map<string, SettingValue>();
    markerMode: { stepPath: string; kind: string } | null = null;
    ackPermanent = false;
    /** s17 park stats choices (sent to the host with each compose). */
    parkStats: { keepVanilla: boolean; overrides: Record<string, number> } = { keepVanilla: true, overrides: {} };
    /** Player's preview choice and the stage it was made on (a new stage starts from its default again). */
    previewChoice: { mode: "sketch" | "ghost"; stage: Stage } | null = null;
    /** s22 legend: the hovered layer ("" = none) and the player's show / hide choices (like C# PlacemakerToolSystem.Legend). */
    legendHover = "";
    legendVisibility: Record<string, boolean> = {};
    /** s22: layer ids the map drew in its last frame (tests check hover-isolate and toggles with it). */
    drawnLayers: string[] = [];

    /** Is a preview layer drawn now? (C# PreviewLegend.Visible) */
    layerVisible(id: string): boolean {
        if (this.legendHover) return id === this.legendHover;
        const v = this.legendVisibility[id];
        return v !== undefined ? v : defaultVisible(id);
    }
    /** Real preview being rebuilt after a change (spinner on the toggle). */
    ghostPending = false;
    ghostMs = 450;
    /** state.planningProgress while planning (the fake planner is instant; tests set it). */
    planProgress = 0.5;
    private ghostTimer: number | null = null;

    // ── plan ──
    plan: ComposeResult | null = null;
    planning = false;
    stale = false;
    private planVersion = 0;
    private planTimer: number | null = null;
    readiness = new Map<string, { ready: boolean; nextStep?: LocText }>();

    // ── build / manage ──
    buildProgress = 0;
    private buildTimer: number | null = null;
    /** What the build under way leaves (the game's AfterBuild): keep the area, close the tool, or a fresh Area step. */
    private afterBuild: "keep" | "close" | "fresh" = "fresh";
    groups: BuiltGroup[] = [];
    managedId: number | null = null;
    editingGroup: number | null = null;
    selectedGroup: number | null = null; // vanilla selection while the tool is closed
    private nextGroupId = 1;

    // ── history ──
    undoStack: Entry[] = [];
    redoStack: Entry[] = [];
    private gesture: Entry | null = null;

    // ── host ──
    hostUp = false;
    hostLayers = new Set<string>();
    hostMs = 0;
    assets: Record<string, unknown[]> = {};
    private listeners = new Set<Listener>();
    toast: string | null = null;

    constructor() {
        addStrings(EN);
        setTriggerHandler((group, name, args) => {
            if (group === GROUP) this.onTrigger(name, args);
        });
        this.root = this.instantiate(this.recipes.find((r) => r.info.id === this.recipeId)!.root);
        this.resetActive();
        this.publishAll();
    }

    onChange(l: Listener) {
        this.listeners.add(l);
        return () => this.listeners.delete(l);
    }

    // ───────────── host integration ─────────────

    async connectHost() {
        try {
            const [cat, lang] = await Promise.all([fetch("/api/catalog").then((r) => r.json()), fetch("/api/lang").then((r) => r.json())]);
            this.hostUp = true;
            if (lang && typeof lang === "object") {
                Object.assign(EN, lang);
                addStrings(lang);
            }
            // Real layers replace fake ones with the same id (case-insensitive: "bigtrees" vs "bigTrees").
            const real: MockLayer[] = (cat?.layers ?? []).map((l: MockLayer) => ({ ...l, real: true }));
            for (const l of real) {
                this.hostLayers.add(l.id);
                this.layers = this.layers.filter((x) => x.id.toLowerCase() !== l.id.toLowerCase());
            }
            this.layers = [...real, ...this.layers];
            if (Array.isArray(cat?.categories)) this.categories = cat.categories;
            // Shipped recipes first; keep fake recipes only where no real layer covers their root.
            const shipped: MockRecipe[] = (cat?.recipes ?? []).map((r: any) => ({
                info: { id: r.id, name: r.name, icon: r.icon, custom: false, description: r.description, group: r.group, category: r.category, shelved: r.shelved },
                root: r.root ?? { layer: r.id },
            }));
            if (shipped.length) {
                const ids = new Set(shipped.map((r) => r.info.id));
                const fake = this.recipes.filter((r) => !ids.has(r.info.id) && !this.hostLayers.has(r.root.layer) && !r.info.custom);
                this.recipes = [...shipped, ...fake, ...this.recipes.filter((r) => r.info.custom)];
                const cur = this.recipes.find((r) => r.info.id === this.recipeId) ?? this.recipes[0];
                this.recipeId = cur.info.id;
                this.root = this.instantiate(cur.root);
                this.focus = "0";
                this.resetActive();
            }
        } catch {
            this.hostUp = false;
        }
        this.publishAll();
        this.schedulePlan(0);
    }

    // ───────────── helpers ─────────────

    layer(id: string): MockLayer | undefined {
        return this.layers.find((l) => l.id === id);
    }

    defaults(layerId: string): Record<string, SettingValue> {
        const out: Record<string, SettingValue> = {};
        const l = this.layer(layerId) as (MockLayer & { defaults?: Record<string, SettingValue> }) | undefined;
        if (l?.defaults) return clone(l.defaults);
        for (const c of l?.controls ?? []) if (c.default !== undefined && c.default !== null && c.kind !== "markers") out[c.id] = clone(c.default);
        return out;
    }

    instantiate(t: RecipeTemplate): StepModel {
        const step: StepModel = {
            layer: t.layer,
            target: t.target ?? "",
            enabled: true,
            values: { ...this.defaults(t.layer), ...(t.values ?? {}) },
            locked: [],
            seed: Math.floor(Math.random() * 1e9),
            markers: [],
            children: (t.children ?? []).map((c) => this.instantiate(c)),
            overrides: Object.fromEntries(Object.entries(t.overrides ?? {}).map(([k, v]) => [k, this.instantiate(v)])),
        };
        for (const f of this.featuresOf(t.layer)) if (f.defaultOn) step.children.push(this.featureStep(f));
        return step;
    }

    /** Features a layer offers (Core IHasFeatures), limited to feature layers this catalog has. */
    featuresOf(layerId: string): FeatureDef[] {
        const own = this.layer(layerId)?.features;
        const defs = own && own.length ? own : FAKE_FEATURES[layerId] ?? FAKE_FEATURES[layerId.toLowerCase()] ?? [];
        return defs.filter((f) => !!this.layer(f.layer));
    }

    featureStep(f: FeatureDef): StepModel {
        const c = this.instantiate({ layer: f.layer, target: f.target ?? "", values: f.preset });
        c.feature = f.layer;
        return c;
    }

    toTemplate(s: StepModel): RecipeTemplate {
        return { layer: s.layer, target: s.target, values: clone(s.values), children: s.children.map((c) => this.toTemplate(c)) };
    }

    /** Same addressing as Core/Tool/RecipePaths: "0", "0/1", "0/1@block:3" (override held by "0"), ".../0". */
    get(path: string): StepModel | null {
        if (!this.root) return null;
        const segs = path.split("/");
        if (segs[0] !== "0") return null;
        let node: StepModel | undefined = this.root;
        for (let i = 1; i < segs.length && node; i++) {
            const at = segs[i].indexOf("@");
            node = at >= 0 ? node.overrides[segs[i].slice(at + 1)] : node.children[Number(segs[i])];
        }
        return node ?? null;
    }

    /** Index of the child an override replaces (informative part of "i@key"). */
    overrideIndex(parent: StepModel, key: string): number {
        const i = parent.children.findIndex((c) => c.target && (c.target === key || key.startsWith(c.target + ":") || c.target === key.split(":")[0]));
        return i < 0 ? 0 : i;
    }

    parentPath(path: string): string | null {
        const slash = path.lastIndexOf("/");
        return slash < 0 ? null : path.slice(0, slash);
    }

    layerName(id: string) {
        const l = this.layer(id);
        return l ? en(l.name, id) : id;
    }

    recipeInfo(): RecipeInfo | undefined {
        return this.recipes.find((r) => r.info.id === this.recipeId)?.info;
    }

    recipeDisplayName() {
        const r = this.recipeInfo();
        if (!r) return this.root ? this.layerName(this.root.layer) : "";
        return r.custom ? r.name : en(r.name, r.id);
    }

    areaValid() {
        return this.closed && this.ring.length >= 3 && !this.problem();
    }

    problem(): LocText | undefined {
        if (!this.closed || this.ring.length < 3) return undefined;
        if (selfIntersects(this.ring)) return { key: "Placemaker.Problem.Crossing" };
        if (area(this.ring) < 150) return { key: "Placemaker.Problem.TooSmall", args: [150] };
        return undefined;
    }

    // ───────────── presets ─────────────

    /** The step's values + enabled features (with their values), as a preset stores them. */
    capture(step: StepModel): PresetData {
        const features: Record<string, Record<string, SettingValue>> = {};
        for (const c of step.children) if (c.feature && c.enabled) features[c.feature] = clone(c.values);
        return { values: clone(step.values), features };
    }

    /** (C# PresetStore.IsTypeStandard) The "standard" style of a fill type that ships none: the type's own design. */
    typeStandard(layerId: string, id: string): boolean {
        const l = this.layer(layerId);
        return id === STANDARD_ID && !!l && l.kind === "fill" && !l.presets.some((p) => p.id === STANDARD_ID);
    }

    hasStandard(layerId: string): boolean {
        return this.typeStandard(layerId, STANDARD_ID) || !!this.layer(layerId)?.presets.some((p) => p.id === STANDARD_ID);
    }

    /** Stored data of a preset: own preset, saved-over built-in, the type's own Standard, or the shipped built-in (its values over the defaults, default features). */
    presetData(layerId: string, id: string): PresetData | null {
        const own = (this.customPresets[layerId] ?? []).find((p) => p.id === id);
        if (own) return clone(own.data);
        const ov = this.presetOverrides[layerId + "|" + id];
        if (ov) return clone(ov);
        if (this.typeStandard(layerId, id)) return this.defaultData(layerId);
        const p = this.layer(layerId)?.presets.find((x) => x.id === id);
        if (!p) return null;
        return { values: { ...this.defaults(layerId), ...clone(p.values ?? {}) }, features: this.defaultData(layerId).features };
    }

    presetName(layerId: string, id: string): string {
        const own = (this.customPresets[layerId] ?? []).find((p) => p.id === id);
        if (own) return own.name;
        if (this.typeStandard(layerId, id)) return en(STANDARD_KEY, "Standard");
        const p = this.layer(layerId)?.presets.find((x) => x.id === id);
        return p ? en(p.name, id) : id;
    }

    applyData(step: StepModel, data: PresetData) {
        step.values = { ...this.defaults(step.layer), ...clone(data.values) };
        step.locked = [];
        step.seed = Math.floor(Math.random() * 1e9);
        if (!data.features) return;
        for (const f of this.featuresOf(step.layer)) {
            const child = step.children.find((c) => c.feature === f.layer);
            const v = data.features[f.layer];
            if (v) {
                const target = child ?? this.featureStep(f);
                target.enabled = true;
                target.values = { ...this.defaults(f.layer), ...clone(v) };
                if (!child) step.children.push(target);
            } else if (child) child.enabled = false;
        }
    }

    /** A type picked (or a build loaded): its Standard; the type's fresh defaults are the baseline, so a loaded build can read "modified". */
    resetActive() {
        this.active = this.root
            ? { id: this.hasStandard(this.root.layer) ? STANDARD_ID : "", layer: this.root.layer, base: this.presetData(this.root.layer, STANDARD_ID) ?? this.defaultData(this.root.layer) }
            : null;
    }

    defaultData(layerId: string): PresetData {
        const r = this.recipes.find((x) => !x.info.custom && x.root.layer === layerId && !(x.root.children ?? []).length);
        return this.capture(this.instantiate(r ? r.root : { layer: layerId }));
    }

    presetViews(l: MockLayer): PresetView[] {
        const list = this.typeStandard(l.id, STANDARD_ID) ? [{ id: STANDARD_ID, name: STANDARD_KEY } as MockLayer["presets"][number], ...l.presets] : l.presets;
        const built: PresetView[] = list.map((p) => ({
            id: p.id, name: p.name, icon: p.icon, overridden: this.presetOverrides[l.id + "|" + p.id] ? true : undefined,
            shelved: (p as PresetView).shelved || styleShelved(l.id, (l as { group?: string }).group, p.id) || undefined,
        }));
        // Standard first, also one the type ships
        built.sort((a, b) => (a.id === STANDARD_ID ? -1 : b.id === STANDARD_ID ? 1 : 0));
        const own: PresetView[] = (this.customPresets[l.id] ?? []).map((p) => ({ id: p.id, name: p.name, custom: true, basedOn: p.basedOn }));
        return [...built, ...own];
    }

    activePreset(): ActivePreset | undefined {
        if (!this.root || !this.active || this.active.layer !== this.root.layer) return undefined;
        const { id, layer, base } = this.active;
        return { id, layer, modified: canon(this.capture(this.root)) !== canon(base), canSave: !!id };
    }

    // ───────────── history ─────────────

    snapshot(): Snapshot {
        return {
            ring: clone(this.ring), closed: this.closed, cells: Array.from(this.cells), picked: this.picked.slice(),
            recipeId: this.recipeId, root: clone(this.root), focus: this.focus, modified: this.modified, active: clone(this.active),
        };
    }

    restore(s: Snapshot) {
        this.ring = clone(s.ring);
        this.closed = s.closed;
        this.cells = new Set(s.cells);
        this.picked = s.picked.slice();
        this.recipeId = s.recipeId;
        this.root = clone(s.root);
        this.focus = s.focus;
        this.modified = s.modified;
        this.active = clone(s.active);
        if (!this.areaValid() && (this.stage === "design" || this.stage === "review")) this.stage = "area";
    }

    /** Run an undoable edit. Same `coalesce` key within 1.2 s merges into one undo step (slider nudges). */
    edit(label: LocText, fn: () => void, coalesce?: string) {
        const now = Date.now();
        const last = this.undoStack[this.undoStack.length - 1];
        const merge = coalesce && last && last.coalesce === coalesce && now - last.at < 1200;
        const before = merge ? last.before : this.snapshot();
        fn();
        if (merge) {
            last.at = now;
        } else {
            this.undoStack.push({ label, before, coalesce, at: now });
            if (this.undoStack.length > 200) this.undoStack.shift();
        }
        this.redoStack = [];
        this.changed();
    }

    /** Gestures (dragging a corner, a brush stroke) record one entry at the end. */
    beginGesture(label: LocText) {
        this.gesture = { label, before: this.snapshot(), at: Date.now() };
    }

    endGesture() {
        if (!this.gesture) return;
        this.undoStack.push(this.gesture);
        this.redoStack = [];
        this.gesture = null;
        this.changed();
    }

    undo() {
        const e = this.undoStack.pop();
        if (!e) return;
        e.after = this.snapshot();
        this.restore(e.before);
        this.redoStack.push(e);
        this.changed();
    }

    redo() {
        const e = this.redoStack.pop();
        if (!e || !e.after) return;
        const before = this.snapshot();
        this.restore(e.after);
        this.undoStack.push({ ...e, before, at: 0 });
        this.changed();
    }

    // ───────────── area editing (map input; the game does this in C#) ─────────────

    addPoint(p: V2) {
        if (this.closed) return;
        if (this.ring.length >= 3 && dist(p, this.ring[0]) < 8) {
            this.edit({ key: "Placemaker.Undo.CloseArea" }, () => (this.closed = true));
            return;
        }
        this.edit({ key: "Placemaker.Undo.AddPoint" }, () => this.ring.push(p));
    }

    closeRing() {
        if (this.ring.length >= 3 && !this.closed) this.edit({ key: "Placemaker.Undo.CloseArea" }, () => (this.closed = true));
    }

    removePoint(i: number) {
        if (i < 0 || i >= this.ring.length) return;
        this.edit({ key: "Placemaker.Undo.RemovePoint" }, () => {
            this.ring.splice(i, 1);
            if (this.ring.length < 3) this.closed = false;
        });
    }

    insertPoint(seg: number, p: V2) {
        this.edit({ key: "Placemaker.Undo.InsertPoint" }, () => this.ring.splice(seg + 1, 0, p));
    }

    movePoint(i: number, p: V2) {
        this.ring[i] = p;
        this.changed(true);
    }

    pickBlock(b: Block, additive: boolean) {
        const idx = BLOCKS.indexOf(b);
        this.edit({ key: additive ? "Placemaker.Undo.AddBlock" : "Placemaker.Undo.SelectBlock" }, () => {
            this.picked = additive ? Array.from(new Set([...this.picked, idx])) : [idx];
            this.ring = blocksRing(this.picked.map((i) => BLOCKS[i]));
            this.closed = true;
        });
    }

    paintAt(p: V2, erase: boolean) {
        paintCells(this.cells, p, this.brush.size, this.brush.shape, this.brush.angle, erase);
        const outline = cellsOutline(this.cells);
        this.ring = outline.length >= 4 ? simplify(outline, 2.2) : [];
        this.closed = this.ring.length >= 3;
        this.changed(true);
    }

    pickNode(i: number) {
        const p = NODES[i];
        if (this.closed) return;
        if (this.ring.length >= 3 && dist(p, this.ring[0]) < 1) {
            this.closeRing();
            return;
        }
        this.edit({ key: "Placemaker.Undo.AddNode" }, () => this.ring.push([p[0], p[1]]));
    }

    addMarker(p: V2) {
        if (!this.markerMode) return;
        const step = this.get(this.markerMode.stepPath);
        if (!step) return;
        const q = closestOnRing(p, this.ring);
        if (q.d > 14) return;
        const kind = this.markerMode.kind;
        const walks = !!this.markerKindsOf(step)?.find((k) => k.kind === kind && k.crosswalk);
        const stored = this.entranceCrosswalk && walks ? kind + "+crosswalk" : kind;
        this.edit({ key: "Placemaker.Undo.AddMarker", args: [en(`Placemaker.Marker.${kind}`, kind)] }, () => step.markers.push({ kind: stored, p: q.q }));
    }

    removeMarkerNear(p: V2) {
        if (!this.markerMode) return;
        const step = this.get(this.markerMode.stepPath);
        if (!step) return;
        const i = step.markers.findIndex((m) => dist(m.p, p) < 10);
        if (i >= 0) this.edit({ key: "Placemaker.Undo.RemoveMarker" }, () => step.markers.splice(i, 1));
    }

    clickGroup(id: number) {
        if (this.open) {
            this.managedId = id;
            this.stage = "manage";
        } else this.selectedGroup = id;
        this.changed();
    }

    /** Test/dev helper: set a ready-made closed area. */
    setArea(ring: V2[]) {
        this.edit({ key: "Placemaker.Undo.CloseArea" }, () => {
            this.ring = ring.map((p) => [p[0], p[1]] as V2);
            this.closed = true;
            this.picked = [];
        });
    }

    // ───────────── triggers (contract.ts Triggers) ─────────────

    onTrigger(name: string, args: unknown[]) {
        const a = args as any[];
        const json = (s: unknown) => {
            try {
                return JSON.parse(String(s));
            } catch {
                return undefined;
            }
        };
        switch (name) {
            case "toggleTool":
                this.open = !this.open;
                if (this.open) this.selectedGroup = null;
                break;
            case "close":
                this.open = false;
                this.markerMode = null;
                break;
            case "setPanelCollapsed":
                this.userCollapsed = !!a[0];
                break;
            case "goToStage":
                if (this.reachable().includes(a[0])) {
                    this.stage = a[0];
                    this.markerMode = null;
                    if (a[0] === "area") this.managedId = null;
                    this.userCollapsed = null;
                }
                break;
            case "primary":
                this.primaryAction();
                return;
            case "buildAndClose": {
                const p = this.primary();
                if (!p.enabled || p.action !== "build" || (this.plan && needsAck(this.plan.permanent) && !this.ackPermanent)) return;
                this.afterBuild = "close";
                this.startBuild();
                this.publishAll();
                return;
            }
            case "undo":
                this.undo();
                return;
            case "redo":
                this.redo();
                return;
            case "setAreaMode":
                // Area overhaul: Road nodes is part of Points.
                this.mode = a[0] === "perimeter" ? "draw" : a[0];
                this.userCollapsed = null;
                break;
            case "setBrush":
                this.brush = { shape: a[0], size: Number(a[1]), angle: Number(a[2]) };
                break;
            case "setBrushRules":
                try {
                    const patch = JSON.parse(String(a[0]));
                    // s43: walls merge per key, like the DLL's ParseBrushRules.
                    this.brushRules = { ...this.brushRules, ...patch, walls: { ...this.brushRules.walls, ...(patch.walls ?? {}) } };
                    // Area overhaul: zoning as one choice sets the three switches, like BrushRules.SetZoningLevel.
                    if (typeof patch.zoning === "number") {
                        const z = patch.zoning;
                        this.brushRules = { ...this.brushRules, zoned: z >= 1, allZoned: z >= 2, emptyZones: z >= 3, zoning: z };
                    }
                } catch { /* ignore */ }
                break;
            case "resetBrushRules":
                this.brushRules = { ...DEFAULT_BRUSH_RULES, walls: { ...DEFAULT_BRUSH_RULES.walls } };
                break;
            case "clearArea":
                this.edit({ key: "Placemaker.Undo.ClearArea" }, () => {
                    this.ring = [];
                    this.closed = false;
                    this.cells.clear();
                    this.picked = [];
                });
                if (this.stage !== "area") this.stage = "area";
                return;
            case "selectRecipe": {
                const r = this.recipes.find((x) => x.info.id === a[0]);
                if (!r) break;
                this.edit({ key: "Placemaker.Undo.SelectRecipe", args: [r.info.custom ? r.info.name : en(r.info.name)] }, () => {
                    this.recipeId = r.info.id;
                    this.root = this.instantiate(r.root);
                    // a type starts on its Standard: one the player saved over is applied (C# SwitchRecipe)
                    const ov = this.presetOverrides[this.root.layer + "|" + STANDARD_ID];
                    if (ov) this.applyData(this.root, clone(ov));
                    this.focus = "0";
                    this.modified = false;
                    this.resetActive();
                });
                return;
            }
            case "focusStep":
                if (this.get(a[0])) this.focus = a[0];
                break;
            case "addLayer": {
                const parent = this.get(a[0]);
                if (!parent || !this.layer(a[1])) break;
                this.edit({ key: "Placemaker.Undo.AddLayer", args: [this.layerName(a[1])] }, () => {
                    parent.children.push(this.instantiate({ layer: a[1], target: a[2] ?? "" }));
                    this.focus = a[0] + "/" + (parent.children.length - 1);
                    this.modified = true;
                });
                return;
            }
            case "removeStep": {
                const pp = this.parentPath(a[0]);
                const parent = pp ? this.get(pp) : null;
                const step = this.get(a[0]);
                if (!parent || !step) break;
                this.edit({ key: "Placemaker.Undo.RemoveLayer", args: [this.layerName(step.layer)] }, () => {
                    const i = parent.children.indexOf(step);
                    if (i >= 0) parent.children.splice(i, 1);
                    for (const k of Object.keys(parent.overrides)) if (parent.overrides[k] === step) delete parent.overrides[k];
                    this.focus = pp!;
                    this.modified = true;
                });
                return;
            }
            case "moveStep": {
                const pp = this.parentPath(a[0]);
                const parent = pp ? this.get(pp) : null;
                const step = this.get(a[0]);
                if (!parent || !step) break;
                const i = parent.children.indexOf(step);
                const j = i + Number(a[1]);
                if (i < 0 || j < 0 || j >= parent.children.length) break;
                this.edit({ key: "Placemaker.Undo.MoveLayer" }, () => {
                    [parent.children[i], parent.children[j]] = [parent.children[j], parent.children[i]];
                    this.focus = pp + "/" + j;
                    this.modified = true;
                });
                return;
            }
            case "setStepEnabled": {
                const step = this.get(a[0]);
                if (!step) break;
                this.edit({ key: a[1] ? "Placemaker.Undo.EnableLayer" : "Placemaker.Undo.DisableLayer", args: [this.layerName(step.layer)] }, () => {
                    step.enabled = !!a[1];
                    this.modified = true;
                });
                return;
            }
            case "setFeature": {
                const step = this.get(String(a[0]));
                const def = step ? this.featuresOf(step.layer).find((f) => f.layer === a[1]) : undefined;
                if (!step || !def) break;
                const on = !!a[2];
                this.edit({ key: on ? "Placemaker.Undo.EnableLayer" : "Placemaker.Undo.DisableLayer", args: [this.layerName(def.layer)] }, () => {
                    const child = step.children.find((c) => c.feature === def.layer);
                    if (child) child.enabled = on;
                    else if (on) step.children.push(this.featureStep(def));
                    this.modified = true;
                });
                return;
            }
            case "setStepTarget": {
                const step = this.get(a[0]);
                if (!step) break;
                this.edit({ key: "Placemaker.Undo.SetTarget", args: [this.layerName(step.layer)] }, () => {
                    step.target = String(a[1] ?? "");
                    this.modified = true;
                });
                return;
            }
            case "overrideRegion": {
                const parent = this.get(a[0]);
                if (!parent) break;
                const key = String(a[1]);
                const layerId = String(a[2] ?? "");
                this.edit({ key: layerId ? "Placemaker.Undo.OverrideRegion" : "Placemaker.Undo.RemoveOverride", args: [key, layerId ? this.layerName(layerId) : ""] }, () => {
                    if (!layerId) delete parent.overrides[key];
                    else {
                        const child = parent.children.find((c) => c.target && (c.target === key || key.startsWith(c.target + ":")));
                        parent.overrides[key] = this.instantiate({ layer: layerId, target: child?.target ?? "" });
                        this.focus = a[0] + "/" + this.overrideIndex(parent, key) + "@" + key;
                    }
                    this.modified = true;
                });
                return;
            }
            case "setValue": {
                const step = this.get(a[0]);
                const v = json(a[2]);
                if (!step || v === undefined) break;
                const label = this.layer(step.layer)?.controls.find((c) => c.id === a[1])?.label ?? a[1];
                this.preview.delete(a[0] + "|" + a[1]);
                this.edit(
                    { key: "Placemaker.Undo.ChangeValue", args: [en(label, a[1])] },
                    () => {
                        step.values[a[1]] = v;
                        if (!step.locked.includes(a[1])) step.locked.push(a[1]);
                        this.modified = true;
                    },
                    a[0] + "|" + a[1],
                );
                void this.applyLinked(step, a[1]);
                return;
            }
            case "previewValue": {
                const v = json(a[2]);
                if (v === undefined) break;
                this.preview.set(a[0] + "|" + a[1], v);
                this.changed(true);
                return;
            }
            case "hoverLegend":
                this.legendHover = String(a[0] ?? "");
                break;
            case "setLegendVisible": {
                const id = String(a[0] ?? "");
                if (!id) break;
                const on = !!a[1];
                if (on === defaultVisible(id)) delete this.legendVisibility[id];
                else this.legendVisibility[id] = on;
                break;
            }
            case "resetLegend":
                this.legendVisibility = {};
                this.legendHover = "";
                break;
            case "setPreviewMode":
                if (a[0] === "sketch" || a[0] === "ghost") {
                    this.previewChoice = { mode: a[0], stage: this.stage };
                    this.touchGhost();
                }
                break;
            case "endPreview":
                this.preview.clear();
                this.changed(true);
                return;
            case "resetStep": {
                const step = this.get(a[0]);
                if (!step) break;
                this.edit({ key: "Placemaker.Undo.ResetLayer", args: [this.layerName(step.layer)] }, () => {
                    step.values = this.defaults(step.layer);
                    step.locked = [];
                    this.modified = true;
                });
                return;
            }
            case "applyPreset": {
                const step = this.get(a[0]);
                const data = step ? this.presetData(step.layer, String(a[1])) : null;
                if (!step || !data) break;
                this.edit({ key: "Placemaker.Undo.ResetLayer", args: [this.presetName(step.layer, String(a[1]))] }, () => {
                    this.applyData(step, data);
                    if (a[0] === "0") this.active = { id: String(a[1]), layer: step.layer, base: this.capture(step) };
                    this.modified = true;
                });
                return;
            }
            case "selectPreset": {
                const layerId = String(a[0]);
                const id = String(a[1] ?? "") || (this.hasStandard(layerId) ? STANDARD_ID : "");
                const r =
                    this.recipes.find((x) => !x.info.custom && x.root.layer === layerId && !(x.root.children ?? []).length) ??
                    this.recipes.find((x) => x.info.id === layerId);
                const data = id ? this.presetData(layerId, id) : null;
                if (!r || (id && !data)) break;
                this.edit({ key: "Placemaker.Undo.SelectRecipe", args: [id ? this.presetName(layerId, id) : this.layerName(layerId)] }, () => {
                    this.recipeId = r.info.id;
                    const root = this.instantiate(r.root);
                    this.root = root;
                    this.focus = "0";
                    if (data) this.applyData(root, data);
                    this.active = { id, layer: root.layer, base: this.capture(root) };
                    this.modified = false;
                });
                return;
            }
            case "savePreset": {
                const step = this.get(String(a[0]));
                const id = String(a[1] ?? "");
                if (!step || !id) break;
                const data = this.capture(step);
                const own = (this.customPresets[step.layer] ?? []).find((p) => p.id === id);
                if (own) own.data = data;
                else if (this.layer(step.layer)?.presets.some((p) => p.id === id) || this.typeStandard(step.layer, id)) this.presetOverrides[step.layer + "|" + id] = data;
                else break;
                if (a[0] === "0") this.active = { id, layer: step.layer, base: clone(data) };
                this.toast = `Saved “${this.presetName(step.layer, id)}”`;
                break;
            }
            case "savePresetAs": {
                const step = this.get(String(a[0]));
                const name = String(a[1] ?? "").trim();
                if (!step || !name) break;
                const id = "custom-" + Date.now().toString(36) + "-" + ++this.presetSeq;
                const from = this.active && this.active.layer === step.layer ? this.active.id : "";
                const basedOn = from && !from.startsWith("custom-") ? from : (this.customPresets[step.layer] ?? []).find((p) => p.id === from)?.basedOn;
                const data = this.capture(step);
                (this.customPresets[step.layer] ??= []).push({ id, name, basedOn, data });
                if (a[0] === "0") this.active = { id, layer: step.layer, base: clone(data) };
                this.toast = `Saved “${name}”`;
                break;
            }
            case "renamePreset": {
                const p = (this.customPresets[String(a[0])] ?? []).find((x) => x.id === a[1]);
                const name = String(a[2] ?? "").trim();
                if (!p || !name) break;
                p.name = name;
                break;
            }
            case "deletePreset": {
                const layerId = String(a[0]);
                const id = String(a[1]);
                const list = this.customPresets[layerId] ?? [];
                const isActive = !!this.active && this.active.layer === layerId && this.active.id === id;
                if (list.some((p) => p.id === id)) {
                    this.customPresets[layerId] = list.filter((p) => p.id !== id);
                    // the design stays, now on the type's Standard (changed if it differs)
                    if (isActive) this.active = { id: this.hasStandard(layerId) ? STANDARD_ID : "", layer: layerId, base: this.presetData(layerId, STANDARD_ID) ?? this.defaultData(layerId) };
                    break;
                }
                const key = layerId + "|" + id;
                if (!this.presetOverrides[key]) break;
                delete this.presetOverrides[key];
                // Reset to default: the active step goes back to the shipped preset (undoable).
                if (isActive && this.root) {
                    const root = this.root;
                    const data = this.presetData(layerId, id)!;
                    this.edit({ key: "Placemaker.Undo.ResetLayer", args: [this.presetName(layerId, id)] }, () => {
                        this.applyData(root, data);
                        this.active = { id, layer: layerId, base: this.capture(root) };
                    });
                    return;
                }
                break;
            }
            case "exportPresets":
                this.presetsFile = PRESETS_FILE;
                this.toast = "Presets exported";
                break;
            case "randomize":
                void this.randomize(String(a[0] ?? ""));
                return;
            case "unlockValue": {
                const step = this.get(a[0]);
                if (!step) break;
                step.locked = step.locked.filter((x) => x !== a[1]);
                break;
            }
            case "startMarkers":
                if (this.markerMode && this.markerMode.stepPath === a[0] && this.markerMode.kind === a[1]) this.markerMode = null;
                else this.markerMode = { stepPath: a[0], kind: a[1] };
                break;
            case "endMarkers":
                this.markerMode = null;
                break;
            case "setMarkerKind":
                // Which entrance type the next click places; also enters placement for that step.
                if (this.get(a[0])) this.markerMode = { stepPath: a[0], kind: String(a[1]) };
                break;
            case "setMarkerCrosswalk": {
                const i = Number(a[1]);
                if (i < 0) { this.entranceCrosswalk = !!a[2]; break; }
                const step = this.get(a[0]);
                if (!step || !(i < step.markers.length)) break;
                const base = step.markers[i].kind.split("+crosswalk")[0];
                this.edit({ key: "Placemaker.Undo.Crosswalk" }, () => (step.markers[i].kind = a[2] ? base + "+crosswalk" : base));
                return;
            }
            case "setBuildUngrouped":
                this.buildSeparate = !!a[0];
                break;
            case "setKeepArea":
                this.keepArea = !!a[0];
                break;
            case "removeMarker": {
                const step = this.get(a[0]);
                const i = Number(a[1]);
                if (!step || !(i >= 0 && i < step.markers.length)) break;
                const kind = step.markers[i].kind;
                this.edit({ key: "Placemaker.Undo.RemoveMarker", args: [en(`Placemaker.Marker.${kind}`, kind)] }, () => step.markers.splice(i, 1));
                return;
            }
            case "clearMarkers": {
                const step = this.get(a[0]);
                if (!step) break;
                this.edit({ key: "Placemaker.Undo.ClearMarkers" }, () => (step.markers = step.markers.filter((m) => m.kind !== a[1])));
                return;
            }
            case "saveRecipeAs": {
                if (!this.root) break;
                const id = "custom-" + Date.now().toString(36);
                const name = String(a[0]);
                this.recipes.push({ info: { id, name, icon: this.layer(this.root.layer)?.icon ?? "custom", custom: true }, root: this.toTemplate(this.root) });
                this.recipeId = id;
                this.modified = false;
                this.toast = `Saved “${name}”`;
                break;
            }
            case "deleteRecipe": {
                this.recipes = this.recipes.filter((r) => r.info.id !== a[0] || !r.info.custom);
                if (this.recipeId === a[0]) {
                    this.recipeId = "";
                    this.modified = true;
                }
                break;
            }
            case "saveAsDefault":
                this.toast = "Saved as default";
                break;
            case "requestAssets":
                window.setTimeout(() => {
                    this.assets[a[0]] = fakeAssets(String(a[0]));
                    this.publishAll();
                }, 120);
                return;
            case "manageGroup":
                this.clickGroup(Number(a[0]));
                return;
            case "editGroup": {
                const g = this.groups.find((x) => x.id === Number(a[0]));
                if (!g) break;
                this.open = true;
                this.selectedGroup = null;
                this.ring = clone(g.ring);
                this.closed = true;
                this.root = clone(g.root);
                this.recipeId = g.recipeId;
                this.focus = "0";
                this.resetActive();
                this.editingGroup = g.id;
                this.managedId = null;
                this.stage = "design";
                this.undoStack = [];
                this.redoStack = [];
                this.changed();
                return;
            }
            case "ungroupGroup":
            // dissolved for good (C# GroupRegistrySystem.Dissolve): the pieces stay, the build is gone from Manage
            case "removeGroup":
                this.groups = this.groups.filter((g) => g.id !== Number(a[0]));
                if (this.managedId === Number(a[0])) {
                    this.managedId = null;
                    if (this.stage === "manage") this.stage = "area";
                }
                if (this.selectedGroup === Number(a[0])) this.selectedGroup = null;
                break;
            case "renameGroup": {
                const g = this.groups.find((x) => x.id === Number(a[0]));
                if (g) g.customName = String(a[1] ?? "");
                break;
            }
            case "setAnarchy":
                this.anarchy.enabled = !!a[0];
                break;
            case "acknowledgePermanent":
                this.ackPermanent = !!a[0];
                break;
            case "setParkStat": {
                const v = Number(JSON.parse(String(a[1] ?? "null")));
                if (Number.isFinite(v)) this.parkStats.overrides[String(a[0])] = v;
                this.schedulePlan(0);
                break;
            }
            case "resetParkStat":
                if (a[0]) delete this.parkStats.overrides[String(a[0])];
                else this.parkStats.overrides = {};
                this.schedulePlan(0);
                break;
            case "setParkStatsKeepVanilla":
                this.parkStats.keepVanilla = !!a[0];
                this.schedulePlan(0);
                break;
            case "locate":
                // contract: locate(x, y, z)
                this.toast = "Camera moved there";
                this.locateAt = [Number(a[0]) || 0, Number(a[1]) || 0, Number(a[2]) || 0];
                break;
            default:
                console.warn("[mock] unknown trigger", name, args);
        }
        this.publishAll();
    }

    locateAt: [number, number, number] | null = null;

    /** s16: like C# UiModels.Step — an automatic asset control the plan does not use is greyed "Not used by this layout". */
    notInLayout(s: StepModel, path: string): Record<string, LocText> | undefined {
        const res = this.plan?.steps?.[path]?.resolvedAssets;
        if (!res) return undefined;
        const out: Record<string, LocText> = {};
        for (const [id, r] of Object.entries(res)) if (r.unused && !s.values[id]) out[id] = { key: "Placemaker.UI.Why.NotInLayout" };
        return Object.keys(out).length ? out : undefined;
    }

    /** s16: linked controls of a real layer (a road mix writes the road picks; the mix follows them), as C# DesignEditor does. */
    async applyLinked(step: StepModel, controlId: string) {
        if (!this.hostLayers.has(step.layer)) return;
        try {
            const r = await fetch("/api/linked", {
                method: "POST",
                headers: { "Content-Type": "application/json" },
                body: JSON.stringify({ layer: step.layer, values: step.values, controlId }),
            }).then((x) => x.json());
            if (!r || r.error || !r.values) return;
            let changed = false;
            for (const [k, v] of Object.entries(r.values as Record<string, SettingValue>))
                if (JSON.stringify(step.values[k]) !== JSON.stringify(v)) {
                    step.values[k] = v;
                    changed = true;
                }
            for (const id of (r.driven as string[]) ?? []) if (!step.locked.includes(id)) step.locked.push(id);
            if (changed) this.changed(true);
        } catch {
            /* host down: no linked controls */
        }
    }

    async randomize(path: string) {
        const steps: StepModel[] = [];
        const walk = (s: StepModel) => {
            steps.push(s);
            s.children.forEach(walk);
            Object.values(s.overrides).forEach(walk);
        };
        if (path === "") {
            if (this.root) walk(this.root);
        } else {
            const s = this.get(path);
            if (s) steps.push(s);
        }
        const results = new Map<StepModel, Record<string, SettingValue>>();
        for (const s of steps) {
            const seed = Math.floor(Math.random() * 1e9);
            let values: Record<string, SettingValue> | null = null;
            if (this.hostLayers.has(s.layer)) {
                try {
                    values = await fetch("/api/randomize", {
                        method: "POST",
                        headers: { "Content-Type": "application/json" },
                        body: JSON.stringify({ layer: s.layer, values: s.values, locked: s.locked, seed }),
                    }).then((r) => r.json());
                } catch {
                    values = null;
                }
            }
            if (!values || (values as any).error) values = this.fakeRandomize(s);
            results.set(s, values);
        }
        this.edit({ key: "Placemaker.Undo.Randomize" }, () => {
            for (const [s, v] of results) {
                s.values = { ...s.values, ...v };
                for (const id of s.locked) if (id in s.values) s.values[id] = s.values[id];
                s.seed = Math.floor(Math.random() * 1e9);
            }
            this.modified = true;
        });
    }

    fakeRandomize(s: StepModel): Record<string, SettingValue> {
        const out: Record<string, SettingValue> = { ...s.values };
        for (const c of this.layer(s.layer)?.controls ?? []) {
            if (s.locked.includes(c.id)) continue;
            if (c.kind === "slider") {
                const st = c.step || 1;
                const n = Math.round(((c.max ?? 1) - (c.min ?? 0)) / st);
                out[c.id] = Number(((c.min ?? 0) + Math.floor(Math.random() * (n + 1)) * st).toFixed(4));
            } else if (c.kind === "choice" && c.options?.length) out[c.id] = c.options[Math.floor(Math.random() * c.options.length)].value;
            else if (c.kind === "toggle" && c.tier === "essential") out[c.id] = Math.random() < 0.6;
        }
        return out;
    }

    // ───────────── stage machine ─────────────

    allReady(): { ready: boolean; path?: string; next?: LocText; kind?: string } {
        let res: { ready: boolean; path?: string; next?: LocText; kind?: string } = { ready: true };
        const visit = (s: StepModel, path: string) => {
            if (!res.ready || !s.enabled) return;
            const r = this.stepReady(s, path);
            if (!r.ready) {
                res = { ready: false, path, next: r.nextStep, kind: this.markerKindsOf(s)?.[0]?.kind };
                return;
            }
            s.children.forEach((c, i) => visit(c, path + "/" + i));
        };
        if (this.root) visit(this.root, "0");
        return res;
    }

    stepReady(s: StepModel, path: string): { ready: boolean; nextStep?: LocText } {
        const l = this.layer(s.layer);
        const need = this.markersRequiredOf(s);
        if (need > 0 && s.markers.length < need) return { ready: false, nextStep: { key: "Placemaker.Parking.NeedsEntrance" } };
        if (l?.real) return this.readiness.get(path) ?? { ready: true };
        return { ready: true };
    }

    /** Typed marker kinds of a step (StepView.markerKinds): declared by the fake layer, or derived from a real layer's markers control. */
    markerKindsOf(s: StepModel): { kind: string; label: string; icon?: string; crosswalk?: boolean }[] | undefined {
        const l = this.layer(s.layer);
        if (!l) return undefined;
        if (l.markerKinds) return l.markerKinds;
        if (l.real) {
            // C# MarkerKinds.For / MarkerVariants.Walks: the kinds of the markers control, walking ones offer a crosswalk
            const c = l.controls.find((x) => x.kind === "markers" && x.markerKinds && x.markerKinds.length);
            const all = c?.markerKinds ?? [];
            const cars = all.some((k) => k === "entry" || k === "exit" || k === "alley");
            const walks = (k: string) => ["pedestrian", "bike", "main", "footpath", "access"].includes(k) || (k === "entrance" && !cars);
            if (c && (l.id.toLowerCase().includes("parking") || all.length > 1)) return all.map((k) => ({ kind: k, label: "", crosswalk: walks(k) || undefined }));
        }
        return undefined;
    }

    markersRequiredOf(s: StepModel): number {
        const l = this.layer(s.layer);
        if (l?.markersRequired !== undefined) return l.markersRequired;
        return this.markerKindsOf(s) ? 1 : 0;
    }

    reachable(): Stage[] {
        if (this.stage === "building") return ["building"];
        const r: Stage[] = ["area"];
        if (this.areaValid()) {
            r.push("design");
            if (this.root) r.push("review");
        }
        if (this.managedId != null) r.push("manage");
        return r;
    }

    primary(): PrimaryAction {
        const summary = this.summary();
        if (this.markerMode && this.stage !== "building") return { action: "placeMarkers", label: { key: "Placemaker.Primary.DoneMarkers" }, enabled: true };
        switch (this.stage) {
            case "area":
                if (this.areaValid()) return { action: "toDesign", label: { key: "Placemaker.Primary.Next" }, enabled: true };
                return {
                    action: "toDesign",
                    label: { key: "Placemaker.Primary.Next" },
                    enabled: false,
                    reason: this.problem() ?? (this.ring.length === 0 ? { key: "Placemaker.Reason.NoArea" } : { key: "Placemaker.Reason.CloseArea" }),
                };
            case "design": {
                if (!this.root) return { action: "toReview", label: { key: "Placemaker.Primary.Review" }, enabled: false, reason: { key: "Placemaker.Reason.NoRecipe" } };
                const r = this.allReady();
                // Like C# StageMachine.MarkerStep: no automatic entrances; disabled, but pressing it starts placement.
                if (!r.ready && r.kind)
                    return { action: "placeMarkers", label: { key: "Placemaker.Primary.Review" }, enabled: false, reason: { key: "Placemaker.Reason.AddEntrance" }, stepPath: r.path, markerKind: r.kind };
                if (!r.ready) return { action: "toReview", label: { key: "Placemaker.Primary.Review" }, enabled: false, reason: r.next };
                return { action: "toReview", label: { key: "Placemaker.Primary.Review" }, enabled: true };
            }
            case "review": {
                const editing = this.editingGroup != null;
                const base = editing
                    ? { action: "finishEdit" as const, label: { key: "Placemaker.Primary.ApplyEdit" } }
                    : { action: "build" as const, label: { key: "Placemaker.Primary.Build", args: [this.recipeDisplayName().toLowerCase()] } };
                const r = this.allReady();
                if (!r.ready && r.kind)
                    return { action: "placeMarkers", label: base.label, enabled: false, reason: { key: "Placemaker.Reason.AddEntrance" }, stepPath: r.path, markerKind: r.kind };
                if (!summary.ready || summary.stale) return { ...base, enabled: false, reason: { key: "Placemaker.Reason.Planning" } };
                if (!r.ready) return { ...base, enabled: false, reason: { key: "Placemaker.Reason.StepNotReady" } };
                if (summary.issues.some((i) => i.severity === "error")) return { ...base, enabled: false, reason: { key: "Placemaker.Reason.PlanErrors" } };
                if (!summary.canAfford) return { ...base, enabled: false, reason: { key: "Placemaker.Reason.NoMoney" } };
                return { ...base, enabled: true };
            }
            case "building":
                return { action: "cancelBuild", label: { key: "Placemaker.Primary.CancelBuild" }, enabled: true };
            case "manage":
                return { action: "none", label: { key: "Placemaker.Primary.PickBuilt" }, enabled: false, reason: { key: "Placemaker.Reason.PickBuilt" } };
        }
        return { action: "none", label: { key: "" }, enabled: false };
    }

    primaryAction() {
        const p = this.primary();
        if (!p.enabled) {
            if (p.action === "placeMarkers" && !this.markerMode && p.stepPath && p.markerKind) {
                if (this.stage === "review") this.stage = "design";
                this.markerMode = { stepPath: p.stepPath, kind: p.markerKind };
                this.focus = p.stepPath;
                this.publishAll();
            }
            return;
        }
        switch (p.action) {
            case "toDesign":
                this.stage = "design";
                break;
            case "toReview":
                this.stage = "review";
                this.markerMode = null;
                break;
            case "placeMarkers": {
                if (this.markerMode) {
                    this.markerMode = null;
                    break;
                }
                const r = this.allReady();
                if (r.path && r.kind) this.markerMode = { stepPath: r.path, kind: r.kind };
                break;
            }
            case "build":
            case "finishEdit":
                if (this.plan && needsAck(this.plan.permanent) && !this.ackPermanent) return;
                this.afterBuild = p.action === "build" ? (this.keepArea ? "keep" : "close") : "fresh";
                this.startBuild();
                break;
            case "cancelBuild":
                if (this.buildTimer) window.clearInterval(this.buildTimer);
                this.buildTimer = null;
                this.stage = "review";
                break;
        }
        this.userCollapsed = null;
        this.publishAll();
    }

    /** Test hook: build speed (ms per 10 %). */
    buildStepMs = 90;

    startBuild() {
        this.stage = "building";
        this.buildProgress = 0;
        this.buildTimer = window.setInterval(() => {
            this.buildProgress = Math.min(1, this.buildProgress + 0.1);
            if (this.buildProgress >= 1) {
                window.clearInterval(this.buildTimer!);
                this.buildTimer = null;
                this.finishBuild();
            }
            this.publishAll();
        }, this.buildStepMs);
    }

    finishBuild() {
        const plan = this.plan!;
        const g: BuiltGroup = {
            id: this.editingGroup ?? this.nextGroupId++,
            ring: clone(this.ring),
            root: clone(this.root!),
            recipeId: this.recipeId,
            recipeName: this.recipeInfo()?.custom ? { key: "Placemaker.UI.Literal", args: [this.recipeDisplayName()] } : { key: this.recipeInfo()?.name ?? this.root!.layer },
            customName: this.groups.find((x) => x.id === this.editingGroup)?.customName ?? "",
            plan,
            stats: plan.stats,
            upkeep: plan.upkeep,
        };
        this.groups = this.groups.filter((x) => x.id !== g.id).concat([g]);
        this.money -= plan.cost;
        this.editingGroup = null;
        const keep = this.afterBuild === "keep";
        if (!keep) {
            this.ring = [];
            this.closed = false;
            this.cells.clear();
            this.picked = [];
        }
        // no type stays chosen (the game: a kept one planned itself on the next area)
        this.root = null;
        this.recipeId = "";
        this.focus = "0";
        this.stage = keep ? "design" : "area";
        if (this.afterBuild === "close") this.open = false;
        this.afterBuild = "fresh";
        this.undoStack = [];
        this.redoStack = [];
        this.ackPermanent = false;
        this.toast = "Built!";
        this.changed();
    }

    allSteps(): StepModel[] {
        const out: StepModel[] = [];
        const walk = (s: StepModel) => {
            out.push(s);
            s.children.forEach(walk);
            Object.values(s.overrides).forEach(walk);
        };
        if (this.root) walk(this.root);
        return out;
    }

    // ───────────── planning ─────────────

    /** Sketch before Review, Real (ghost) on Review — unless the player chose on this stage. */
    previewMode(): "sketch" | "ghost" {
        if (this.previewChoice && this.previewChoice.stage === this.stage) return this.previewChoice.mode;
        return this.stage === "review" || this.stage === "building" ? "ghost" : "sketch";
    }

    /** The ghost (real models + textures) takes a moment to rebuild after each change. */
    touchGhost() {
        if (this.previewMode() !== "ghost") return;
        this.ghostPending = true;
        if (this.ghostTimer != null) window.clearTimeout(this.ghostTimer);
        this.ghostTimer = window.setTimeout(() => {
            this.ghostTimer = null;
            this.ghostPending = false;
            this.publishAll();
        }, this.ghostMs);
    }

    /** Ground colour of the real preview: the root step's surface (hover-previewed value included). */
    ghostSurfaceColor(): string {
        const root = this.effectiveRoot();
        const layer = root ? this.layer(root.layer) : undefined;
        const c = layer?.controls.find((x) => x.kind === "asset" && (x.assetCategory ?? "").startsWith("surface"));
        if (!root || !c) return "#7d8a7a";
        const v = root.values[c.id];
        const id = typeof v === "string" && v ? v : defaultSurface(c.assetCategory ?? "surface");
        return surfaceColor(id) ?? "#7d8a7a";
    }

    /** Adds the types other workstreams are bringing (botanical garden, dog park…) to show the Make row at scale. */
    addMoreTypes() {
        const extra: Record<string, string> = {};
        for (const [id, name, icon, group] of MORE_TYPES) {
            if (this.layers.some((l) => l.id === id)) continue;
            const key = "Placemaker." + id.charAt(0).toUpperCase() + id.slice(1) + ".Name";
            extra[key] = name;
            this.layers.push({
                id, name: key, icon, kind: "fill", accepts: [], emits: ["green"], presets: [], minArea: 400,
                controls: [
                    { id: "ground", kind: "asset", tier: "essential", group: "", label: "Placemaker.Park.Ground", assetCategory: id === "marketSquare" ? "surface.paving" : "surface.grass", default: "" },
                    { id: "trees", kind: "slider", tier: "essential", group: "", label: "Placemaker.Park.TreeDensity", min: 0, max: 100, step: 5, unit: "%", default: 50 },
                ],
            });
            this.recipes.push({ info: { id, name: key, icon, custom: false, group }, root: { layer: id } });
        }
        Object.assign(EN, extra);
        addStrings(extra);
        this.publishAll();
    }

    changed(cheap = false) {
        this.touchGhost();
        this.stale = true;
        this.schedulePlan(cheap ? 30 : 60);
        this.publishAll();
    }

    effectiveRoot(): StepModel | null {
        if (!this.root) return null;
        if (!this.preview.size) return this.root;
        const r = clone(this.root);
        const saved = this.root;
        this.root = r;
        for (const [k, v] of this.preview) {
            const [path, id] = k.split("|");
            const s = this.get(path);
            if (s) s.values[id] = v;
        }
        this.root = saved;
        return r;
    }

    schedulePlan(delay: number) {
        if (this.planTimer) window.clearTimeout(this.planTimer);
        // Bump now so a plan already in flight can't land after this edit (latest request wins).
        const version = ++this.planVersion;
        this.planTimer = window.setTimeout(() => void this.runPlan(version), delay);
    }

    stepToJson(s: StepModel): unknown {
        const ov: Record<string, unknown> = {};
        for (const k of Object.keys(s.overrides)) ov[k] = this.stepToJson(s.overrides[k]);
        return { layer: s.layer, target: s.target, enabled: s.enabled, values: s.values, locked: s.locked, seed: s.seed, markers: s.markers, children: s.children.map((c) => this.stepToJson(c)), overrides: ov };
    }

    async runPlan(version: number) {
        const root = this.effectiveRoot();
        if (!root || !this.areaValid()) {
            this.plan = null;
            this.planning = false;
            this.stale = false;
            this.publishAll();
            return;
        }
        this.planning = true;
        this.publishAll();
        const layersUsed = this.allSteps().map((s) => s.layer);
        const useHost = this.hostUp && layersUsed.every((l) => this.hostLayers.has(l));
        let plan: ComposeResult;
        if (useHost) {
            try {
                const t0 = performance.now();
                plan = await fetch("/api/compose", {
                    method: "POST",
                    headers: { "Content-Type": "application/json" },
                    body: JSON.stringify({ recipe: this.stepToJson(root), ring: this.ring, parkStats: this.parkStats }),
                }).then((r) => r.json());
                plan.source = "core";
                this.hostMs = performance.now() - t0;
                await this.refreshReadiness(root);
            } catch (e) {
                console.warn("[mock] host compose failed, using fake planner", e);
                plan = composeFake(root, this.ring);
            }
        } else {
            plan = composeFake(root, this.ring);
            await new Promise((r) => window.setTimeout(r, 40));
        }
        if (version !== this.planVersion) return;
        this.plan = plan;
        this.planning = false;
        this.stale = false;
        this.publishAll();
    }

    async refreshReadiness(root: StepModel) {
        const jobs: Promise<void>[] = [];
        const visit = (s: StepModel, path: string) => {
            if (this.hostLayers.has(s.layer))
                jobs.push(
                    fetch("/api/ready", {
                        method: "POST",
                        headers: { "Content-Type": "application/json" },
                        body: JSON.stringify({ layer: s.layer, values: s.values, ring: this.ring, markers: s.markers }),
                    })
                        .then((r) => r.json())
                        .then((r) => {
                            this.readiness.set(path, { ready: r.ready !== false, nextStep: r.nextStep ? { key: r.nextStep, args: r.args } : r.reason ? { key: r.reason, args: r.args } : undefined });
                        })
                        .catch(() => undefined),
                );
            s.children.forEach((c, i) => visit(c, path + "/" + i));
        };
        visit(root, "0");
        await Promise.all(jobs);
    }

    // ───────────── published values ─────────────

    collapsed(): boolean {
        if (this.userCollapsed !== null) return this.userCollapsed;
        return this.stage === "area" && this.mode === "draw" && this.ring.length > 0 && !this.closed;
    }

    hints(): Hint[] {
        const h = (key: string, keys?: string[]): Hint => ({ text: { key }, keys });
        if (this.markerMode) return [h("Placemaker.Hint.MarkerPlace", ["Click"]), h("Placemaker.Hint.MarkerRemove", ["Right-click"]), h("Placemaker.Hint.MarkerDone", ["Esc"])];
        switch (this.stage) {
            case "area":
                switch (this.mode) {
                    case "draw":
                        return this.closed
                            ? [h("Placemaker.Hint.DrawEdit", ["Drag"]), h("Placemaker.Hint.DrawInsertClick", ["Ctrl", "Click"]), h("Placemaker.Hint.DrawRemoveLast", ["Right-click"]), h("Placemaker.Hint.Primary", ["Enter"])]
                            : [h("Placemaker.Hint.DrawAdd", ["Click"]), h("Placemaker.Hint.DrawAddOrClose", ["Click first"]), h("Placemaker.Hint.DrawRemoveLast", ["Right-click"]), h("Placemaker.Hint.Undo", ["Ctrl", "Z"])];
                    case "block":
                        return [h("Placemaker.Hint.BlockPick", ["Click"]), h("Placemaker.Hint.BlockAdd", ["Shift", "Click"]), h("Placemaker.Hint.Primary", ["Enter"])];
                    case "paint":
                        return [h("Placemaker.Hint.PaintPaint", ["Left drag"]), h("Placemaker.Hint.PaintErase", ["Right drag"]), h("Placemaker.Hint.PaintRotate", ["Shift", "Move"]), h("Placemaker.Hint.Primary", ["Enter"])];
                    case "blockPaint":
                        return [h("Placemaker.Hint.BlockPaintStart", ["Left drag"]), h("Placemaker.Hint.BlockPaintErase", ["Right drag"]), h("Placemaker.Hint.PaintRotate", ["Shift", "Move"]), h("Placemaker.Hint.Primary", ["Enter"])];
                    case "perimeter":
                        return [h("Placemaker.Hint.PerimeterAdd", ["Click"]), h("Placemaker.Hint.DrawAddOrClose", ["Click first"]), h("Placemaker.Hint.Undo", ["Ctrl", "Z"])];
                }
                return [];
            case "design":
                return [h("Placemaker.Hint.Undo", ["Ctrl", "Z"]), h("Placemaker.Hint.Primary", ["Enter"]), h("Placemaker.Hint.Close", ["Esc"])];
            case "review":
                return [h("Placemaker.Hint.Primary", ["Enter"]), h("Placemaker.Hint.Undo", ["Ctrl", "Z"]), h("Placemaker.Hint.Close", ["Esc"])];
            default:
                return [];
        }
    }

    state(): ToolState {
        const last = this.undoStack[this.undoStack.length - 1];
        const next = this.redoStack[this.redoStack.length - 1];
        return {
            open: this.open,
            stage: this.stage,
            reachable: this.reachable(),
            areaMode: this.mode,
            brush: { ...this.brush, rules: this.brushRules },
            area: {
                valid: this.areaValid(),
                closed: this.closed,
                pointCount: this.ring.length,
                areaM2: this.closed && this.ring.length >= 3 ? Math.round(area(this.ring)) : 0,
                perimeterM: this.ring.length >= 2 ? Math.round(perimeter(this.ring)) : 0,
                problem: this.problem(),
            },
            primary: this.primary(),
            canUndo: this.undoStack.length > 0 && this.stage !== "building",
            canRedo: this.redoStack.length > 0 && this.stage !== "building",
            undoLabel: last?.label,
            redoLabel: next?.label,
            markerMode: this.markerMode ? { ...this.markerMode, label: { key: "Placemaker.Marker." + this.markerMode.kind } } : undefined,
            hints: this.hints(),
            planning: this.planning,
            buildProgress: this.stage === "building" ? this.buildProgress : undefined,
            editingGroup: this.editingGroup ?? undefined,
            anarchy: this.anarchy,
            grouping: { separate: this.buildSeparate },
            entranceCrosswalk: this.entranceCrosswalk,
            keepArea: this.keepArea,
            panelCollapsed: this.collapsed(),
            previewMode: this.previewMode(),
            planningProgress: this.planning ? this.planProgress : undefined,
            ghostPending: this.previewMode() === "ghost" && this.ghostPending,
            protocol: this.protocol,
            areaModes: this.areaModes,
            legendVisibility: Object.keys(this.legendVisibility).length ? { ...this.legendVisibility } : undefined,
            areaLegend: this.stage === "area" ? this.areaLegend() : undefined,
        };
    }

    /** Area overhaul: what the area tools draw (C# Core/Tool/AreaLegend), a fixed sample per tool. */
    areaLegend(): ToolState["areaLegend"] {
        const e = (id: string, color: string, count: number, areaM2?: number) => ({ id, label: "Placemaker.Legend.Area." + id.slice(5), color, count, areaM2 });
        const shape = this.closed && this.ring.length >= 3 ? Math.round(area(this.ring)) : 0;
        const list: NonNullable<ToolState["areaLegend"]> = [];
        if (this.mode === "block") {
            if (shape) list.push(e("area:outline", "#cceaff", 1, shape));
            list.push(e("area:fill", "#ffffff", 1), e("area:doors", "#ffcc33", 1), e("area:stops", "#ff5a46", 4));
        } else if (this.mode === "blockPaint" || this.mode === "paint") {
            if (shape) list.push(e("area:painted", "#4d9eff", 1, shape));
            list.push(e("area:fence", "#e6f2ff", 1), e("area:stops", "#ff5a46", 3));
        } else if (shape) list.push(e("area:outline", "#cceaff", 1, shape));
        return list.length ? list : undefined;
    }

    catalog(): Catalog {
        return {
            layers: this.layers.map(({ minArea, markerKinds, markersRequired, real, features, ...l }) => ({ ...l, presets: this.presetViews(l as MockLayer) })) as any,
            recipes: this.recipes.map((r) => shelve(r.info)),
            categories: this.categories,
        };
    }

    /** (core) The central shelf's categories (the host's when it is up, else Recipes/shelf/*.json bundled). */
    categories: CategoryInfo[] = SHELF_CATEGORIES;

    /** Tests: force a feature's fit status by feature layer id (null = computed). */
    fitOverride: Record<string, FeatureFit | null> = {};

    /** Mirrors C# UiModels.FeatureFit: did the enabled feature at childPath fit in the latest plan? */
    featureFit(parentPath: string, childPath: string, layer: string): FeatureFit | undefined {
        const forced = this.fitOverride[layer];
        if (forced) return forced;
        const p = this.plan;
        if (!p) return undefined;
        let parentRan = false, runs = 0, failed = 0, reduced = 0;
        let firstFail: Issue | undefined, firstReduced: Issue | undefined;
        const rank = { info: 0, warning: 1, error: 2 } as const;
        for (const l of p.layers) {
            if (l.stepPath.startsWith(parentPath + "@")) { parentRan = true; continue; }
            if (!l.stepPath.startsWith(childPath + "@") || l.layer !== layer) continue;
            runs++;
            let worst: Issue | undefined;
            for (const i of l.issues) if (!i.text.key.includes("Permanent") && (!worst || rank[i.severity] > rank[worst.severity])) worst = i;
            const placed = l.objects.length + l.nets.length + l.surfaces.length + l.areas.length + l.terraform.length + l.zoning.length + l.regions.length > 0;
            if (!placed || worst?.severity === "error") {
                failed++;
                firstFail = firstFail ?? worst;
            } else if (worst) {
                reduced++;
                firstReduced = firstReduced ?? worst;
            }
        }
        const stale = this.stale || undefined;
        if (!runs) return !parentRan || this.stale ? undefined : { kind: "failed", text: { key: "Placemaker.UI.Feature.NoRoom" } };
        if (failed === runs) return { kind: "failed", text: firstFail?.text ?? { key: "Placemaker.UI.Feature.NoRoom" }, stale };
        if (failed) return { kind: "reduced", text: { key: "Placemaker.UI.Feature.SomeMissing", args: [runs - failed, runs] }, stale };
        if (reduced) return { kind: "reduced", text: firstReduced!.text, stale };
        return { kind: "ok", stale };
    }

    private featureTooBig(layerId: string): LocText | undefined {
        const min = (this.layer(layerId) as any)?.minArea as number | undefined;
        if (!min || !this.closed || this.ring.length < 3) return undefined;
        return area(this.ring) < min ? { key: "Placemaker.Why.AreaTooSmall", args: [Math.round(min)] } : undefined;
    }

    stepView(s: StepModel, path: string): StepView {
        const overrides: Record<string, StepView> = {};
        for (const k of Object.keys(s.overrides)) overrides[k] = this.stepView(s.overrides[k], path + "/" + this.overrideIndex(s, k) + "@" + k);
        const regions: StepView["regions"] = [];
        const seen = new Set<string>();
        for (const l of this.plan?.layers ?? []) {
            if (l.stepPath.split("@")[0] !== path) continue;
            for (const r of l.regions) if (!seen.has(r.key)) {
                seen.add(r.key);
                regions.push({ key: r.key, tags: r.tags, areaM2: Math.round(r.areaM2) });
            }
        }
        const counts = new Map<string, number>();
        for (const m of s.markers) { const k = m.kind.split("+crosswalk")[0]; counts.set(k, (counts.get(k) ?? 0) + 1); }
        const kinds = new Set<string>();
        for (const c of this.layer(s.layer)?.controls ?? []) if (c.kind === "markers") (c.markerKinds ?? [c.id]).forEach((k) => kinds.add(k));
        for (const k of this.markerKindsOf(s) ?? []) kinds.add(k.kind);
        const r = this.stepReady(s, path);
        const values = { ...s.values };
        for (const [k, v] of this.preview) {
            const [p, id] = k.split("|");
            if (p === path) values[id] = v;
        }
        const features = this.featuresOf(s.layer).map((f) => {
            const i = s.children.findIndex((c) => c.feature === f.layer);
            const on = i >= 0 && s.children[i].enabled;
            return {
                layer: f.layer, label: f.labelKey ?? "", icon: this.layer(f.layer)?.icon ?? f.layer, enabled: on, path: on ? path + "/" + i : undefined,
                tooltip: f.tooltip || undefined, options: f.options && f.options.length ? f.options : undefined, defaultOn: !!f.defaultOn,
                status: on ? this.featureFit(path, path + "/" + i, f.layer) : undefined,
                // s15: like C# Availability — a feature that needs more room than the whole selection is greyed with why.
                disabled: this.featureTooBig(f.layer),
            };
        });
        return {
            path,
            layer: s.layer,
            features: features.length ? features : undefined,
            target: s.target,
            enabled: s.enabled,
            values,
            locked: s.locked.slice(),
            markers: Array.from(kinds).map((k) => ({ kind: k, count: counts.get(k) ?? 0 })),
            markerList: s.markers.map((m, index) => ({ kind: m.kind.split("+crosswalk")[0], index, position: [m.p[0], 0, m.p[1]] as [number, number, number], crosswalk: m.kind.endsWith("+crosswalk") || undefined })),
            markerKinds: this.markerKindsOf(s),
            markersRequired: this.markersRequiredOf(s),
            ready: r.ready,
            nextStep: r.nextStep,
            children: s.children.map((c, i) => this.stepView(c, path + "/" + i)),
            overrides,
            regions,
            resolvedAssets: this.plan?.steps?.[path]?.resolvedAssets,
            netLegend: this.plan?.steps?.[path]?.netLegend?.length ? this.plan?.steps?.[path]?.netLegend : undefined,
            disabled: this.notInLayout(s, path),
        };
    }

    design(): DesignState {
        return {
            recipeId: this.recipeId, modified: this.modified, root: this.root ? this.stepView(this.root, "0") : null, focus: this.focus,
            activePreset: this.activePreset(), presetsFile: this.presetsFile,
        };
    }

    summary(): Summary {
        const p = this.plan;
        if (!p) return { ready: false, stale: this.stale, stats: [], cost: 0, upkeep: 0, canAfford: true, layers: [], issues: [], permanent: [] };
        const issues = [...p.issues, ...p.layers.flatMap((l) => l.issues)];
        return {
            ready: true,
            stale: this.stale,
            stats: p.stats,
            cost: p.cost,
            upkeep: p.upkeep,
            canAfford: p.cost <= this.money,
            layers: p.perLayer,
            issues,
            permanent: p.permanent,
            permanentNeedsAck: needsAck(p.permanent),
            parkStats: p.parkStats ?? undefined,
            costBuckets: p.costBuckets ?? undefined,

            legend: p.legend?.length ? p.legend : fakeLegend(p, this.root ? this.allSteps().reduce((n, st) => n + st.markers.length, 0) : 0),
        };
    }

    managed(): ManagedGroup | null {
        const g = this.groups.find((x) => x.id === this.managedId);
        if (!g) return null;
        return { id: g.id, recipeName: g.recipeName, customName: g.customName || undefined, stats: g.stats, upkeep: g.upkeep, canEdit: true };
    }

    publishAll() {
        // JSON strings, as ValueBinding<string> would deliver them.
        publish(GROUP, "state", JSON.stringify(this.state()));
        publish(GROUP, "catalog", JSON.stringify(this.catalog()));
        publish(GROUP, "design", JSON.stringify(this.design()));
        publish(GROUP, "summary", JSON.stringify(this.summary()));
        publish(GROUP, "assets", JSON.stringify(this.assets));
        publish(GROUP, "managed", JSON.stringify(this.managed()));
        const stage = this.state().stage;
        publish(GROUP, "sitePlan", JSON.stringify(this.plan?.sitePlan && (stage === "design" || stage === "review") ? { ...(this.plan.sitePlan as object), stale: this.stale || undefined } : null));
        this.listeners.forEach((l) => l());
    }
}

export const FAKE_IDS = FAKE_LAYER_IDS;
