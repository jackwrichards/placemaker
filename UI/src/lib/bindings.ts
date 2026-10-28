// Bindings to Game/UI/PlacemakerUISystem (shapes: ../contract.ts, which is authoritative).
//
// Robustness rules:
//  * every value tolerates a missing/late C# binding (fallback = null → defaults below);
//  * values may arrive as raw JSON objects (RawValueBinding) or as JSON strings (ValueBinding<string>);
//    both are accepted and parsing is guarded — a malformed payload yields defaults, never a crash;
//  * partial payloads are merged over defaults, so a C# side that lags one field behind still renders.
import { useMemo } from "react";
import { guardAiLeave } from "./aiLeave";
import { bindValue, trigger, useValue, ValueBinding } from "cs2/api";
import {
    AssetLists, Catalog, DesignState, FeatureFit, GROUP, ManagedGroup, Stage, StepView, Summary, ToolState, Triggers, Values,
    AreaMode, BrushShape, SettingValue, BrushRules, BucketWalls, LocText, NetLegendEntry, ResolvedAsset, LegendEntry, SitePlanView, AiView,
} from "../contract";

const LOG = "[Placemaker]";

function bindRaw(name: string): ValueBinding<unknown> | null {
    try {
        return bindValue<unknown>(GROUP, name, null);
    } catch (e) {
        console.warn(LOG, "binding unavailable", name, e);
        return null;
    }
}

export const bindings = {
    state: bindRaw(Values.state),
    catalog: bindRaw(Values.catalog),
    design: bindRaw(Values.design),
    summary: bindRaw(Values.summary),
    assets: bindRaw(Values.assets),
    managed: bindRaw(Values.managed),
    sitePlan: bindRaw(Values.sitePlan),
    ai: bindRaw(Values.ai),
};

const NULL_BINDING: ValueBinding<unknown> = {
    value: null,
    subscribe: () => ({ value: null, setChangeListener: () => undefined, dispose: () => undefined }),
    dispose: () => undefined,
};

export function parseJson(raw: unknown): unknown {
    if (typeof raw !== "string") return raw;
    if (raw === "") return null;
    try {
        return JSON.parse(raw);
    } catch (e) {
        console.warn(LOG, "bad JSON from C#", e);
        return null;
    }
}

const isObj = (v: unknown): v is Record<string, any> => v != null && typeof v === "object" && !Array.isArray(v);
function arr<T>(v: unknown): T[] {
    return Array.isArray(v) ? (v as T[]) : [];
}

// ───────────── defaults & normalisers ─────────────

export const DEFAULT_STATE: ToolState = {
    open: false,
    stage: "area",
    reachable: ["area"],
    areaMode: "draw",
    brush: { shape: "round", size: 40, angle: 0 },
    area: { valid: false, closed: false, pointCount: 0, areaM2: 0, perimeterM: 0 },
    primary: { action: "none", label: { key: "Placemaker.UI.Primary.SelectArea" }, enabled: false },
    canUndo: false,
    canRedo: false,
    hints: [],
    planning: false,
    panelCollapsed: false,
};

export function normalizeState(raw: unknown): ToolState {
    const v = parseJson(raw);
    if (!isObj(v)) return DEFAULT_STATE;
    return {
        ...DEFAULT_STATE,
        ...v,
        reachable: arr<Stage>(v.reachable).length ? arr<Stage>(v.reachable) : ["area"],
        brush: { ...DEFAULT_STATE.brush, ...(isObj(v.brush) ? v.brush : {}) },
        area: { ...DEFAULT_STATE.area, ...(isObj(v.area) ? v.area : {}) },
        primary: { ...DEFAULT_STATE.primary, ...(isObj(v.primary) ? v.primary : {}) },
        hints: arr(v.hints),
        // s15 (additive): absent = a DLL older than s15.
        protocol: typeof v.protocol === "number" ? v.protocol : undefined,
        areaModes: Array.isArray(v.areaModes)
            ? arr<any>(v.areaModes)
                  .filter((m) => isObj(m) && typeof m.mode === "string")
                  .map((m) => ({ mode: m.mode, enabled: m.enabled !== false, reason: normalizeLoc(m.reason) }))
            : undefined,
        legendVisibility: normalizeVisibility(v.legendVisibility),
    } as ToolState;
}

/** s22 additive: layer id → visible; non-boolean entries dropped, absent when empty. */
function normalizeVisibility(v: unknown): Record<string, boolean> | undefined {
    if (!isObj(v)) return undefined;
    const out: Record<string, boolean> = {};
    for (const k of Object.keys(v)) if (typeof v[k] === "boolean") out[k] = v[k];
    return Object.keys(out).length ? out : undefined;
}

/** s22 additive: preview legend entries; malformed entries dropped, absent when empty. */
export function normalizePreviewLegend(v: unknown): LegendEntry[] | undefined {
    const num = (x: unknown) => (typeof x === "number" && isFinite(x) ? x : undefined);
    const str = (x: unknown) => (typeof x === "string" && x ? x : undefined);
    const out = arr<any>(v)
        .filter((e) => isObj(e) && typeof e.id === "string" && e.id)
        .map((e) => ({
            id: String(e.id),
            label: String(e.label ?? ""),
            color: typeof e.color === "string" ? e.color : "#cccccc",
            color2: str(e.color2),
            count: Number(e.count) || 0,
            lengthM: num(e.lengthM),
            areaM2: num(e.areaM2),
            minDelta: num(e.minDelta),
            maxDelta: num(e.maxDelta),
            defaultOff: e.defaultOff === true || undefined,
            group: str(e.group),
            assetId: str(e.assetId),
            assetName: str(e.assetName),
            control: str(e.control),
        }));
    return out.length ? out : undefined;
}

export const EMPTY_CATALOG: Catalog = { layers: [], recipes: [] };

export function normalizeCatalog(raw: unknown): Catalog {
    const v = parseJson(raw);
    if (!isObj(v)) return EMPTY_CATALOG;
    return {
        layers: arr<any>(v.layers).filter(isObj).map((l) => ({
            ...l,
            accepts: arr(l.accepts),
            emits: arr(l.emits),
            controls: arr<any>(l.controls).filter(isObj).map((c) => ({ ...c, options: arr(c.options) })),
            presets: arr<any>(l.presets).filter((x) => isObj(x) && typeof x.id === "string" && x.id),
        })),
        recipes: arr<any>(v.recipes).filter(isObj),
        // (core) The central shelf's categories in Make-row order (Recipes/shelf/*.json).
        categories: arr<any>(v.categories).filter((c) => isObj(c) && typeof c.id === "string" && c.id),
    } as Catalog;
}

export const EMPTY_DESIGN: DesignState = { recipeId: "", modified: false, root: null, focus: "0" };

export function normalizeDesign(raw: unknown): DesignState {
    const v = parseJson(raw);
    if (!isObj(v)) return EMPTY_DESIGN;
    const ap = isObj(v.activePreset) ? v.activePreset : null;
    return {
        ...EMPTY_DESIGN,
        ...v,
        root: isObj(v.root) ? normalizeStep(v.root) : null,
        activePreset: ap
            ? { id: String(ap.id ?? ""), layer: String(ap.layer ?? ""), modified: ap.modified === true, canSave: ap.canSave === true }
            : undefined,
        presetsFile: typeof v.presetsFile === "string" && v.presetsFile ? v.presetsFile : undefined,
    } as DesignState;
}

/** A LocText (additive fields): dropped unless it has a key. */
function normalizeLoc(v: unknown): LocText | undefined {
    return isObj(v) && typeof v.key === "string" && v.key ? { key: v.key, args: arr<any>(v.args) } : undefined;
}

/** (s15) Control id → why it does not apply to this area. */
function normalizeDisabled(v: unknown): Record<string, LocText> | undefined {
    if (!isObj(v)) return undefined;
    const out: Record<string, LocText> = {};
    for (const k of Object.keys(v)) {
        const l = normalizeLoc(v[k]);
        if (l) out[k] = l;
    }
    return Object.keys(out).length ? out : undefined;
}

/** Feature fit status (additive field): dropped unless well-formed. */
function normalizeFit(v: unknown): FeatureFit | undefined {
    if (!isObj(v) || (v.kind !== "ok" && v.kind !== "reduced" && v.kind !== "failed")) return undefined;
    const text = isObj(v.text) && typeof v.text.key === "string" && v.text.key ? { key: v.text.key, args: arr<any>(v.text.args) } : undefined;
    return { kind: v.kind, text, stale: v.stale === true || undefined };
}

function normalizeStep(s: Record<string, any>): StepView {
    const overrides: Record<string, StepView> = {};
    if (isObj(s.overrides)) for (const k of Object.keys(s.overrides)) if (isObj(s.overrides[k])) overrides[k] = normalizeStep(s.overrides[k]);
    return {
        path: String(s.path ?? "0"),
        layer: String(s.layer ?? ""),
        target: String(s.target ?? ""),
        enabled: s.enabled !== false,
        values: isObj(s.values) ? s.values : {},
        locked: arr<string>(s.locked),
        markers: arr(s.markers),
        markerList: arr<any>(s.markerList)
            .filter(isObj)
            .map((m, i) => ({ kind: String(m.kind ?? ""), index: typeof m.index === "number" ? m.index : i, position: (Array.isArray(m.position) ? m.position : [0, 0, 0]) as [number, number, number], crosswalk: m.crosswalk === true || undefined })),
        markerKinds: arr<any>(s.markerKinds)
            .map((k) => (typeof k === "string" ? { kind: k, label: "" } : k))
            .filter((k) => isObj(k) && typeof k.kind === "string"),
        markersRequired: Number(s.markersRequired) || 0,
        features: arr<any>(s.features)
            .filter((f) => isObj(f) && typeof f.layer === "string" && f.layer)
            .map((f) => ({ layer: String(f.layer), label: String(f.label ?? ""), icon: String(f.icon ?? ""), enabled: f.enabled === true, path: typeof f.path === "string" && f.path ? f.path : undefined, tooltip: typeof f.tooltip === "string" && f.tooltip ? f.tooltip : undefined, options: arr<any>(f.options).filter((o) => typeof o === "string"), defaultOn: f.defaultOn === true, status: normalizeFit(f.status), disabled: normalizeLoc(f.disabled) })),
        ready: s.ready !== false,
        nextStep: s.nextStep,
        children: arr<any>(s.children).filter(isObj).map(normalizeStep),
        overrides,
        regions: arr(s.regions),
        resolvedAssets: normalizeResolved(s.resolvedAssets),
        netLegend: normalizeLegend(s.netLegend),
        disabled: normalizeDisabled(s.disabled),
    };
}

/** s16 additive: control id → resolved prefab; malformed entries dropped, absent when empty. */
function normalizeResolved(v: unknown): Record<string, ResolvedAsset> | undefined {
    if (!isObj(v)) return undefined;
    const out: Record<string, ResolvedAsset> = {};
    for (const k of Object.keys(v)) {
        const r = v[k];
        if (!isObj(r) || typeof r.id !== "string" || !r.id) continue;
        out[k] = { id: r.id, name: typeof r.name === "string" && r.name ? r.name : undefined, icon: typeof r.icon === "string" && r.icon ? r.icon : undefined, unused: r.unused === true || undefined };
    }
    return Object.keys(out).length ? out : undefined;
}

/** s16 additive: road legend entries; malformed entries dropped, absent when empty. */
function normalizeLegend(v: unknown): NetLegendEntry[] | undefined {
    const out = arr<any>(v)
        .filter((e) => isObj(e) && typeof e.role === "string" && e.role)
        .map((e) => ({
            role: String(e.role),
            label: String(e.label ?? ""),
            color: typeof e.color === "string" ? e.color : "#cccccc",
            count: Number(e.count) || 0,
            lengthM: Number(e.lengthM) || 0,
            assetId: typeof e.assetId === "string" && e.assetId ? e.assetId : undefined,
            assetName: typeof e.assetName === "string" && e.assetName ? e.assetName : undefined,
            control: typeof e.control === "string" && e.control ? e.control : undefined,
        }));
    return out.length ? out : undefined;
}

export const EMPTY_SUMMARY: Summary = {
    ready: false, stale: false, stats: [], cost: 0, upkeep: 0, canAfford: true, layers: [], issues: [], permanent: [],
};

export function normalizeSummary(raw: unknown): Summary {
    const v = parseJson(raw);
    if (!isObj(v)) return EMPTY_SUMMARY;
    return {
        ...EMPTY_SUMMARY,
        ...v,
        stats: arr(v.stats),
        layers: arr<any>(v.layers).filter(isObj).map((l) => ({ ...l, stats: arr(l.stats) })),
        issues: arr<any>(v.issues).filter(isObj),
        permanent: arr(v.permanent),
        cost: Number(v.cost) || 0,
        upkeep: Number(v.upkeep) || 0,
        canAfford: v.canAfford !== false,
        legend: normalizePreviewLegend(v.legend),
        costBuckets: Array.isArray(v.costBuckets)
            ? arr<any>(v.costBuckets).filter((b) => isObj(b) && typeof b.id === "string").map((b) => ({ id: b.id, cost: Number(b.cost) || 0, upkeep: Number(b.upkeep) || 0 }))
            : undefined,
    } as Summary;
}

export function normalizeAssets(raw: unknown): AssetLists {
    const v = parseJson(raw);
    if (!isObj(v)) return {};
    const out: AssetLists = {};
    for (const k of Object.keys(v)) out[k] = arr<any>(v[k]).filter((a) => isObj(a) && typeof a.id === "string");
    return out;
}

export function normalizeManaged(raw: unknown): ManagedGroup | null {
    const v = parseJson(raw);
    if (!isObj(v) || typeof v.id !== "number") return null;
    return {
        ...v,
        stats: arr(v.stats),
        recipeName: isObj(v.recipeName) ? v.recipeName : { key: String(v.recipeName ?? "") },
    } as ManagedGroup;
}

function useBound<T>(binding: ValueBinding<unknown> | null, normalize: (raw: unknown) => T): T {
    const raw = useValue(binding ?? NULL_BINDING);
    return useMemo(() => normalize(raw), [raw]);
}

export const useToolState = () => useBound(bindings.state, normalizeState);
export const useCatalog = () => useBound(bindings.catalog, normalizeCatalog);
export const useDesign = () => useBound(bindings.design, normalizeDesign);
export const useSummary = () => useBound(bindings.summary, normalizeSummary);
export const useAssets = () => useBound(bindings.assets, normalizeAssets);
export const useManaged = () => useBound(bindings.managed, normalizeManaged);

/** (sketch study) The site plan of the current plan, or null (no plan, older DLL, malformed payload). */
export function normalizeSitePlan(raw: unknown): SitePlanView | null {
    const v = parseJson(raw);
    if (!isObj(v) || !Array.isArray(v.outline) || v.outline.length < 6) return null;
    const list = (k: string) => arr<any>(v[k]);
    return {
        ...v,
        stamp: String(v.stamp ?? ""),
        min: list("min"), max: list("max"), centre: list("centre"), outline: list("outline"),
        fills: list("fills"), cars: list("cars"), walks: list("walks"), streets: list("streets"), caps: list("caps"), fences: list("fences"),
        stalls: list("stalls"), chargers: list("chargers"), props: list("props"), propShadows: list("propShadows"),
        trees: list("trees"), bushes: list("bushes"), plots: list("plots"), entrances: list("entrances"), issues: list("issues"), legend: list("legend"),
        areaM2: typeof v.areaM2 === "number" ? v.areaM2 : 0,
    } as SitePlanView;
}

export const useSitePlan = () => useBound(bindings.sitePlan, normalizeSitePlan);

function normalizeAi(raw: unknown): AiView | null {
    if (raw == null) return null;
    try {
        const v = (typeof raw === "string" ? JSON.parse(raw) : raw) as AiView | null;
        if (!v || typeof v !== "object") return null;
        const d: Partial<AiView> = { phase: "", round: 0, startedAt: 0, model: "", thinking: "", mods: false, modsLocked: false };
        return { ...d, ...v, lines: Array.isArray(v.lines) ? v.lines : [], stepArgs: Array.isArray(v.stepArgs) ? v.stepArgs : [] };
    } catch {
        return null;
    }
}

/** The AI category's chat; null while the AI builder option is off. */
export const useAi = () => useBound(bindings.ai, normalizeAi);

// ───────────── triggers ─────────────

function fire(name: string, ...args: unknown[]) {
    try {
        trigger(GROUP, name, ...args);
    } catch (e) {
        console.warn(LOG, "trigger failed", name, e);
    }
}

const json = (v: SettingValue) => JSON.stringify(v);

/** Every UI → C# call, typed after contract.ts Triggers. */
export const act = {
    toggleTool: () => guardAiLeave(() => fire(Triggers.toggleTool)),
    togglePolish: () => fire(Triggers.togglePolish),
    close: () => guardAiLeave(() => fire(Triggers.close)),
    setPanelCollapsed: (collapsed: boolean) => fire(Triggers.setPanelCollapsed, collapsed),
    goToStage: (stage: Stage) => (stage === "area" ? guardAiLeave(() => fire(Triggers.goToStage, stage)) : fire(Triggers.goToStage, stage)),
    primary: () => fire(Triggers.primary),
    /** Review's second button: build, then close Placemaker (the primary builds and keeps the area). */
    buildAndClose: () => fire(Triggers.buildAndClose),
    /** (PROTOCOL 25) The painter: Polish (true) or Unpolish (false) the lots in the area. */
    polishStamp: (yes: boolean) => fire(Triggers.polishStamp, yes),
    /** The theme the painter's Polish gives. */
    polishTheme: (theme: string) => fire(Triggers.polishTheme, theme),
    /** A look row of the painter card (toggles 1 / 0, painted 0–100). */
    polishOption: (id: string, value: number) => fire(Triggers.polishOption, id, value),
    /** A Whole city row of the painter card. */
    polishCity: (what: string, value = "") => fire(Triggers.polishCity, what, value),
    /** A setting of the look picked in the polish tool. */
    polishKnob: (id: string, value: number) => fire(Triggers.polishKnob, id, value),
    /** save / saveAs (name) / rename (name) / delete / revert the look picked. */
    polishLook: (what: string, arg = "") => fire(Triggers.polishLook, what, arg),
    /** A zone type's look ("off"; "" = as its kind). */
    polishZone: (zone: string, look: string) => fire(Triggers.polishZone, zone, look),
    /** The Live switch beside Polish. */
    polishLive: (on: boolean) => fire(Triggers.polishLive, on),
    undo: () => fire(Triggers.undo),
    redo: () => fire(Triggers.redo),
    setAreaMode: (mode: AreaMode) => fire(Triggers.setAreaMode, mode),
    setBrush: (shape: BrushShape, size: number, angle: number) => fire(Triggers.setBrush, shape, size, angle),
    setBrushRules: (rules: Partial<Omit<BrushRules, "walls">> & { walls?: Partial<BucketWalls> }) => fire(Triggers.setBrushRules, JSON.stringify(rules)),
    resetBrushRules: () => fire(Triggers.resetBrushRules),
    clearArea: () => guardAiLeave(() => fire(Triggers.clearArea)),
    selectRecipe: (id: string) => (id === "ai" ? fire(Triggers.selectRecipe, id) : guardAiLeave(() => fire(Triggers.selectRecipe, id))),
    focusStep: (path: string) => fire(Triggers.focusStep, path),
    addLayer: (parentPath: string, layerId: string, target: string) => fire(Triggers.addLayer, parentPath, layerId, target),
    removeStep: (path: string) => fire(Triggers.removeStep, path),
    moveStep: (path: string, delta: number) => fire(Triggers.moveStep, path, delta),
    setStepEnabled: (path: string, enabled: boolean) => fire(Triggers.setStepEnabled, path, enabled),
    setStepTarget: (path: string, target: string) => fire(Triggers.setStepTarget, path, target),
    overrideRegion: (parentPath: string, regionKey: string, layerId: string) =>
        fire(Triggers.overrideRegion, parentPath, regionKey, layerId),
    setValue: (path: string, id: string, v: SettingValue) => fire(Triggers.setValue, path, id, json(v)),
    aiSend: (text: string) => fire(Triggers.aiSend, text),
    aiStop: () => fire(Triggers.aiStop),
    aiReset: () => fire(Triggers.aiReset),
    aiMods: (on: boolean) => fire(Triggers.aiMods, on),
    previewValue: (path: string, id: string, v: SettingValue) => fire(Triggers.previewValue, path, id, json(v)),
    endPreview: () => fire(Triggers.endPreview),
    resetStep: (path: string) => fire(Triggers.resetStep, path),
    applyPreset: (path: string, presetId: string) => fire(Triggers.applyPreset, path, presetId),
    randomize: (path: string) => fire(Triggers.randomize, path),
    unlockValue: (path: string, id: string) => fire(Triggers.unlockValue, path, id),
    startMarkers: (path: string, kind: string) => fire(Triggers.startMarkers, path, kind),
    clearMarkers: (path: string, kind: string) => fire(Triggers.clearMarkers, path, kind),
    saveRecipeAs: (name: string) => fire(Triggers.saveRecipeAs, name),
    deleteRecipe: (id: string) => fire(Triggers.deleteRecipe, id),
    saveAsDefault: (path: string) => fire(Triggers.saveAsDefault, path),
    requestAssets: (category: string) => fire(Triggers.requestAssets, category),
    manageGroup: (id: number) => guardAiLeave(() => fire(Triggers.manageGroup, id)),
    editGroup: (id: number) => guardAiLeave(() => fire(Triggers.editGroup, id)),
    removeGroup: (id: number) => fire(Triggers.removeGroup, id),
    renameGroup: (id: number, name: string) => fire(Triggers.renameGroup, id, name),
    setAnarchy: (enabled: boolean) => fire(Triggers.setAnarchy, enabled),
    /** Move the camera to a world position (issue, marker). */
    locate: (at: [number, number, number]) => fire(Triggers.locate, Number(at[0]) || 0, Number(at[1]) || 0, Number(at[2]) || 0),
    setMarkerKind: (path: string, kind: string) => fire(Triggers.setMarkerKind, path, kind),
    removeMarker: (path: string, index: number) => fire(Triggers.removeMarker, path, index),
    /** Crosswalk or not: index -1 = the entrances placed next, else that placed marker. */
    setMarkerCrosswalk: (path: string, index: number, on: boolean) => fire(Triggers.setMarkerCrosswalk, path, index, on),
    ungroupGroup: (id: number) => fire(Triggers.ungroupGroup, id),
    setBuildUngrouped: (on: boolean) => fire(Triggers.setBuildUngrouped, on),
    setKeepArea: (on: boolean) => fire(Triggers.setKeepArea, on),
    /** Leave marker placement: in marker mode the C# primary action is "placeMarkers" (Done), which toggles it off. */
    endMarkers: () => fire(Triggers.primary),
    /** The player ticked the permanent-effects box (C# also guards Enter with it). */
    acknowledgePermanent: (ack: boolean) => fire(Triggers.acknowledgePermanent, ack),
    /** Turn a built-in feature of a fill step on/off (adds/enables its child step in C#). */
    setFeature: (path: string, featureLayerId: string, enabled: boolean) => fire(Triggers.setFeature, path, featureLayerId, enabled),
    /** Map preview: fast sketch overlay or the real translucent ghost (models + surfaces). */
    setPreviewMode: (mode: "sketch" | "ghost") => fire(Triggers.setPreviewMode, mode),
    /** Select a type (its single-layer recipe) and apply one of its presets. */
    selectPreset: (layerId: string, presetId: string) => fire(Triggers.selectPreset, layerId, presetId),
    /** Overwrite a custom preset, or save over a built-in, with the step's current settings + features. */
    savePreset: (path: string, presetId: string) => fire(Triggers.savePreset, path, presetId),
    savePresetAs: (path: string, name: string) => fire(Triggers.savePresetAs, path, name),
    renamePreset: (layerId: string, presetId: string, name: string) => fire(Triggers.renamePreset, layerId, presetId, name),
    /** Custom: delete. Saved-over built-in: reset to its shipped settings. */
    deletePreset: (layerId: string, presetId: string) => fire(Triggers.deletePreset, layerId, presetId),
    exportPresets: () => fire(Triggers.exportPresets),
    /** s17: override one park stat for this build (undoable, no re-plan). */
    setParkStat: (id: string, value: number) => fire(Triggers.setParkStat, id, JSON.stringify(value)),
    /** s17: back to the suggested value; "" = every stat. */
    resetParkStat: (id: string) => fire(Triggers.resetParkStat, id),
    /** s17: clamp park stats to the range vanilla parks of this size use. */
    setParkStatsKeepVanilla: (keep: boolean) => fire(Triggers.setParkStatsKeepVanilla, keep),
    /** s22: isolate one preview layer on the map while its legend entry is hovered ("" = none). */
    hoverLegend: (layerId: string) => fire(Triggers.hoverLegend, layerId),
    /** s22: show / hide a preview layer (kept for the game session). */
    setLegendVisible: (layerId: string, visible: boolean) => fire(Triggers.setLegendVisible, layerId, visible),
    /** s22: every preview layer back to its default. */
    resetLegend: () => fire(Triggers.resetLegend),
    /** s53: mark on the map where the pieces of one "Left out" reason would have been ("" = none). */
    hoverAudit: (reasonId: string) => fire(Triggers.hoverAudit, reasonId),
};

/** Vanilla UI sound through the game's audio binding; silently ignored where it doesn't exist. */
export function playSound(sound: string) {
    try {
        trigger("audio", "playSound", sound, 1);
    } catch {
        /* no audio binding (mock) */
    }
}
