// The Placemaker panel: docked on the RIGHT, tall (below the top bar → above the bottom toolbar),
// draggable by its header (position remembered; double-click the header to dock it again).
//   header  — drag grip, name, undo/redo, collapse, close
//   stages  — [1 Area] → [2 Design] → [3 Review] (click any reachable stage to go back)
//   body    — the current stage, scrolls inside the panel (each region behind its own ErrorBoundary)
//   footer  — what you get / why you can't continue yet + the primary button (always the next step, Enter)
//   hints   — shortcut strip from state.hints
// While drawing, C# may collapse it to a slim header + primary button (state.panelCollapsed); it then
// shows an obvious "Show options" button.
import { Legend } from "./Legend";
import { AiLeaveGuard } from "./AiChat";
import { SitePlanFlyout, SitePlanWindow, useSitePlanBig } from "./SitePlanCard";
import React, { useCallback, useEffect, useLayoutEffect, useMemo, useRef, useState } from "react";
import classNames from "classnames";
import { PROTOCOL, Stage, StepView, Summary, ToolState } from "../contract";
import { act, useCatalog, useDesign, useManaged, useSitePlan, useSummary, useToolState } from "../lib/bindings";
import { area as fmtArea, compactMoney, formatValue } from "../lib/format";
import { useT } from "../lib/l10n";
import { ErrorBoundary } from "../lib/errorBoundary";
import { useRtl } from "../lib/rtl";
import { Tip, TipDirection, TipDirectionContext } from "../lib/tips";
import { statIcon } from "../lib/icons";
import { walk } from "../lib/schema";
import { requestReveal } from "../lib/reveal";
import { AreaStage } from "../stages/AreaStage";
import { DesignStage, markerLabel } from "../stages/DesignStage";
import { BuildingStage, ReviewStage } from "../stages/ReviewStage";
import { ManageStage } from "../stages/ManageStage";
import { Btn, Check, Icon, Keys, Segmented, Switch } from "./common";
import { layoutFor, MAX_SHARE, MIN_REM, NARROW_REM, PanelLayoutContext, WIDE_DEFAULT_REM, WIDE_SCREEN_PX } from "../lib/layout";
import { AssetPickerCard, PickerContext, PickerRequest } from "./picker";
import { PolishCityCard, PolishDesign, useLookEditorRequests } from "./PainterCard";
import { ScrollArea } from "./ScrollArea";
import { DialogLayerContext } from "./dialog";
import s from "./panel.module.scss";

const STAGES: { id: Stage; key: string; en: string }[] = [
    { id: "area", key: "Placemaker.UI.Stage.Area", en: "Area" },
    { id: "design", key: "Placemaker.UI.Stage.Design", en: "Design" },
    { id: "review", key: "Placemaker.UI.Stage.Review", en: "Review" },
];

// ───────────── position (dock / drag) and width (resize) ─────────────

const POS_KEY = "placemaker.panelPos.v2";
const WIDTH_KEY = "placemaker.panelWidth.v2";   // v2: the narrower default (450) for everyone once
const HEIGHT_KEY = "placemaker.panelHeight.v1";
/** Smallest panel height in rem (header + a few rows). */
const MIN_HEIGHT_REM = 200;
/** Docked insets in rem (keep in sync with .wrap): below the top bar, above the bottom toolbar. */
const DOCK_TOP = 64;
const DOCK_BOTTOM = 112;

/** Floating position as fractions of the viewport (survives resolution / UI-scale changes). */
type Pos = { fx: number; fy: number } | null;

function loadPos(): Pos {
    try {
        const raw = window.localStorage?.getItem(POS_KEY);
        if (!raw) return null;
        const v = JSON.parse(raw);
        return v && typeof v.fx === "number" && typeof v.fy === "number" ? { fx: v.fx, fy: v.fy } : null;
    } catch {
        return null;
    }
}

function savePos(p: Pos) {
    try {
        if (p) window.localStorage?.setItem(POS_KEY, JSON.stringify(p));
        else window.localStorage?.removeItem(POS_KEY);
    } catch {
        /* storage unavailable: the position is simply not remembered */
    }
}

/** The player's chosen width in rem, or null (= the default for this screen). */
function loadWidth(): number | null {
    try {
        const raw = window.localStorage?.getItem(WIDTH_KEY);
        const v = raw ? Number(raw) : NaN;
        return isFinite(v) && v > 0 ? v : null;
    } catch {
        return null;
    }
}

function saveWidth(w: number | null) {
    try {
        if (w) window.localStorage?.setItem(WIDTH_KEY, String(Math.round(w)));
        else window.localStorage?.removeItem(WIDTH_KEY);
    } catch {
        /* not remembered */
    }
}

/** The player's chosen height in rem, or null (= docked height: top bar to bottom toolbar). */
function loadHeight(): number | null {
    try {
        const raw = window.localStorage?.getItem(HEIGHT_KEY);
        const v = raw ? Number(raw) : NaN;
        return isFinite(v) && v > 0 ? v : null;
    } catch {
        return null;
    }
}

function saveHeight(h: number | null) {
    try {
        if (h) window.localStorage?.setItem(HEIGHT_KEY, String(Math.round(h)));
        else window.localStorage?.removeItem(HEIGHT_KEY);
    } catch {
        /* not remembered */
    }
}

/** Default width: wider on big screens (≥ 1600 px), the classic 400rem otherwise. */
const defaultWidth = () => (window.innerWidth >= WIDE_SCREEN_PX ? WIDE_DEFAULT_REM : NARROW_REM);

function isInsideButton(el: HTMLElement | null, stop: HTMLElement | null) {
    for (let e = el; e && e !== stop; e = e.parentElement) if (e.tagName === "BUTTON" || e.tagName === "INPUT") return true;
    return false;
}

/**
 * Pixels per rem as the panel is really drawn (rendered width / width in rem), measured after every render. The root
 * font size is only the fallback: in the game it does not match the UI scale, which made the width follow the mouse at
 * the wrong speed and let the height grow past the screen.
 */
let measuredRemPx = 0;
function remPx() {
    if (measuredRemPx > 0) return measuredRemPx;
    const fs = parseFloat(getComputedStyle(document.documentElement).fontSize);
    return fs > 0 ? fs : window.innerHeight / 1080;
}

function usePanelPosition() {
    const [pos, setPos] = useState<Pos>(loadPos);
    const [drag, setDrag] = useState<{ x: number; y: number } | null>(null);
    const [chosen, setChosen] = useState<number | null>(loadWidth);
    const [resizing, setResizing] = useState<number | null>(null);
    const [chosenH, setChosenH] = useState<number | null>(loadHeight);
    const [resizingH, setResizingH] = useState<number | null>(null);
    const [, bump] = useState(0);
    const wrapRef = useRef<HTMLDivElement | null>(null);

    useEffect(() => {
        const onResize = () => bump((n) => n + 1);
        window.addEventListener("resize", onResize);
        return () => window.removeEventListener("resize", onResize);
    }, []);

    /** Width limits in rem: at least MIN_REM, at most MAX_SHARE of the screen. */
    const maxWidth = () => Math.max(MIN_REM, Math.floor((window.innerWidth * MAX_SHARE) / remPx()));
    const clampWidth = (w: number) => Math.max(MIN_REM, Math.min(maxWidth(), Math.round(w)));
    const width = clampWidth(resizing ?? chosen ?? defaultWidth());

    // The real pixels per rem, from the panel as drawn: mouse moves (pixels) and sizes (rem) use the same scale. Not while
    // it is dragged or resized: the drawn width lags a frame behind, the scale read then jumped and the width with it
    // ("glitchy when I drag it out far").
    useLayoutEffect(() => {
        if (resizing !== null || resizingH !== null || drag) return;
        const w = wrapRef.current?.getBoundingClientRect().width ?? 0;
        if (w > 0 && width > 0) measuredRemPx = w / width;
    });

    /** Height in rem: the chosen one (or the docked height), never taller than the screen. */
    const dockedHeight = () => Math.max(MIN_HEIGHT_REM, window.innerHeight / remPx() - (DOCK_TOP + DOCK_BOTTOM));
    const maxHeight = () => Math.max(MIN_HEIGHT_REM, Math.floor((window.innerHeight - 16) / remPx()));
    const clampHeight = (h: number) => Math.max(MIN_HEIGHT_REM, Math.min(maxHeight(), Math.round(h)));
    const height = clampHeight(resizingH ?? chosenH ?? dockedHeight());

    /** Clamped pixel position: the whole panel stays on screen; moving never changes its height. */
    const clamp = (x: number, y: number, w = width, h = height) => {
        const r = remPx();
        return {
            x: Math.max(0, Math.min(window.innerWidth - w * r, x)),
            y: Math.max(0, Math.min(window.innerHeight - h * r - 8, y)),
        };
    };

    const onHeaderMouseDown = (e: React.MouseEvent<HTMLElement>) => {
        if (e.button !== 0 || isInsideButton(e.target as HTMLElement, e.currentTarget)) return;
        const rect = wrapRef.current?.getBoundingClientRect();
        if (!rect) return;
        e.preventDefault();
        const sx = e.clientX,
            sy = e.clientY;
        let moved = false;
        const move = (ev: MouseEvent) => {
            const dx = ev.clientX - sx,
                dy = ev.clientY - sy;
            if (!moved && Math.abs(dx) + Math.abs(dy) < 4) return;
            moved = true;
            setDrag(clamp(rect.left + dx, rect.top + dy));
        };
        const up = (ev: MouseEvent) => {
            window.removeEventListener("mousemove", move);
            window.removeEventListener("mouseup", up);
            if (!moved) return;
            const c = clamp(rect.left + ev.clientX - sx, rect.top + ev.clientY - sy);
            const p = { fx: c.x / window.innerWidth, fy: c.y / window.innerHeight };
            savePos(p);
            setPos(p);
            setDrag(null);
        };
        window.addEventListener("mousemove", move);
        window.addEventListener("mouseup", up);
    };

    /** Drag the left edge: the right edge stays put (docked: the screen edge), the width follows. */
    const onResizeMouseDown = (e: React.MouseEvent<HTMLElement>) => {
        if (e.button !== 0) return;
        const rect = wrapRef.current?.getBoundingClientRect();
        if (!rect) return;
        e.preventDefault();
        e.stopPropagation();
        const r = remPx();
        const sx = e.clientX;
        const start = width;
        const floating = !!pos;
        // the limits fixed for the whole drag; a floating panel stops widening at the screen's left edge (it used to be
        // pushed back to the right, its edge running away from the mouse)
        const cap = Math.max(MIN_REM, Math.min(maxWidth(), floating ? Math.floor(rect.right / r) : Number.MAX_SAFE_INTEGER));
        const at = (ev: MouseEvent) => Math.max(MIN_REM, Math.min(cap, Math.round(start + (sx - ev.clientX) / r)));
        const place = (w: number) => (floating ? clamp(rect.right - w * r, rect.top, w) : null);
        const move = (ev: MouseEvent) => {
            const w = at(ev);
            setResizing(w);
            const c = place(w);
            if (c) setDrag(c);
        };
        const up = (ev: MouseEvent) => {
            window.removeEventListener("mousemove", move);
            window.removeEventListener("mouseup", up);
            const w = at(ev);
            saveWidth(w);
            setChosen(w);
            setResizing(null);
            const c = place(w);
            if (c) {
                const p = { fx: c.x / window.innerWidth, fy: c.y / window.innerHeight };
                savePos(p);
                setPos(p);
                setDrag(null);
            }
        };
        window.addEventListener("mousemove", move);
        window.addEventListener("mouseup", up);
    };

    /** Drag the bottom edge: the top stays put, the height follows. */
    const onResizeHeightMouseDown = (e: React.MouseEvent<HTMLElement>) => {
        if (e.button !== 0) return;
        const rect = wrapRef.current?.getBoundingClientRect();
        if (!rect) return;
        e.preventDefault();
        e.stopPropagation();
        const r = remPx();
        const sy = e.clientY;
        const start = rect.height / r;
        const limit = (window.innerHeight - rect.top - 8) / r;
        const at = (ev: MouseEvent) => Math.max(MIN_HEIGHT_REM, Math.min(limit, Math.round(start + (ev.clientY - sy) / r)));
        const move = (ev: MouseEvent) => setResizingH(at(ev));
        const up = (ev: MouseEvent) => {
            window.removeEventListener("mousemove", move);
            window.removeEventListener("mouseup", up);
            const h = at(ev);
            saveHeight(h);
            setChosenH(h);
            setResizingH(null);
        };
        window.addEventListener("mousemove", move);
        window.addEventListener("mouseup", up);
    };

    /** Double-click the bottom edge: back to the full docked height. */
    const resetHeight = () => {
        saveHeight(null);
        setChosenH(null);
    };

    /** Double-click the edge: back to the default width for this screen. */
    const resetWidth = () => {
        saveWidth(null);
        setChosen(null);
    };

    const reset = () => {
        savePos(null);
        setPos(null);
        setDrag(null);
    };

    const r = remPx();
    let style: React.CSSProperties = { width: width + "rem" };
    let centerX = window.innerWidth; // docked right
    // Docked with a chosen height: the top stays under the top bar, the bottom ends where the player dragged it.
    if (chosenH !== null || resizingH !== null) style = { ...style, bottom: "auto", height: height + "rem" };
    if (drag || pos) {
        const c = drag ?? clamp((pos as { fx: number }).fx * window.innerWidth, (pos as { fy: number }).fy * window.innerHeight);
        // Floating: its own height (chosen, or the docked height); moving never changes it.
        style = { ...style, left: c.x + "px", top: c.y + "px", right: "auto", bottom: "auto", height: height + "rem" };
        centerX = c.x + (width * r) / 2;
    }
    const side: TipDirection = centerX > window.innerWidth / 2 ? "left" : "right";
    return {
        wrapRef,
        style,
        floating: !!(drag || pos),
        dragging: !!drag && resizing === null,
        resizing: resizing !== null,
        resizingHeight: resizingH !== null,
        onResizeHeightMouseDown,
        resetHeight,
        onHeaderMouseDown,
        onResizeMouseDown,
        resetWidth,
        reset,
        side,
        width,
    };
}

// ───────────── pieces ─────────────

/** The polish tool's steps: pick buildings, then polish them (nothing is planned or reviewed). */
const POLISH_STAGES: { id: Stage; key: string; en: string }[] = [
    { id: "area", key: "Placemaker.UI.Stage.Area", en: "Area" },
    { id: "design", key: "Placemaker.UI.Polish.StagePolish", en: "Outgrow" },
];

const StageBar = ({ state }: { state: ToolState }) => {
    const { t } = useT();
    const STAGES_SHOWN = state.painter?.tool ? POLISH_STAGES : STAGES;
    const currentIndex = STAGES_SHOWN.findIndex((x) => x.id === (state.stage === "building" ? "review" : state.stage));
    const locked = state.stage === "building";
    return (
        <div className={s.stageBar} data-testid="stage-bar">
            {STAGES_SHOWN.map((st, i) => {
                const current = i === currentIndex;
                const done = currentIndex > i;
                const reachable = !locked && !current && state.reachable.includes(st.id);
                return (
                    <button
                        key={st.id}
                        type="button"
                        data-testid={`stagebar-${st.id}`}
                        data-state={current ? "current" : reachable ? "reachable" : "locked"}
                        className={classNames(s.stageItem, {
                            [s.stageCurrent]: current,
                            [s.stageReachable]: reachable,
                            [s.stageLocked]: !current && !reachable,
                        })}
                        onClick={(e) => {
                            e.stopPropagation();
                            if (reachable) act.goToStage(st.id);
                        }}
                    >
                        <div className={classNames(s.stageNum, { [s.stageNumDone]: done })}>{done ? <Icon name="check" size={10} tint="dark" /> : i + 1}</div>
                        <div className={s.stageLabel}>{t(st.key, st.en)}</div>
                    </button>
                );
            })}
        </div>
    );
};

/** The preview mode in effect: C#'s choice, else Real on Review and Sketch before it. */
export function effectivePreviewMode(state: ToolState): "sketch" | "ghost" {
    if (state.previewMode === "sketch" || state.previewMode === "ghost") return state.previewMode;
    return state.stage === "review" || state.stage === "building" ? "ghost" : "sketch";
}

/** Sketch | Real: fast overlay or the real translucent models + textures (spinner while the ghost rebuilds). */
const PreviewToggle = ({ state }: { state: ToolState }) => {
    const { t } = useT();
    const mode = effectivePreviewMode(state);
    const pending = mode === "ghost" && !!state.ghostPending;
    const opts: { value: "sketch" | "ghost"; icon: string; label: string; title: string; tip: string }[] = [
        {
            value: "sketch",
            icon: "sketch",
            label: t("Placemaker.UI.Preview.Sketch", "Sketch"),
            title: t("Placemaker.UI.Preview.SketchTitle", "Sketch preview"),
            tip: t("Placemaker.UI.Preview.SketchTip", "Fast outlines and dots. Best while drawing and trying things."),
        },
        {
            value: "ghost",
            icon: "cube",
            label: t("Placemaker.UI.Preview.Real", "Real"),
            title: t("Placemaker.UI.Preview.RealTitle", "Real preview"),
            tip: t("Placemaker.UI.Preview.RealTip", "See-through game models and textures, exactly what will be built."),
        },
    ];
    return (
        <div className={s.previewToggle} data-testid="preview-mode" data-mode={mode} data-pending={pending ? "true" : "false"}>
            {opts.map((o) => {
                const on = o.value === mode;
                return (
                    <Tip key={o.value} title={o.title} description={o.tip}>
                        <button
                            type="button"
                            data-testid={`preview-mode-${o.value}`}
                            aria-pressed={on ? "true" : "false"}
                            className={classNames(s.pvBtn, { [s.pvOn]: on })}
                            onClick={(e) => {
                                e.stopPropagation();
                                if (!on) act.setPreviewMode(o.value);
                            }}
                        >
                            {o.value === "ghost" && pending ? (
                                <div className={s.spinner} data-testid="ghost-pending" />
                            ) : (
                                <Icon name={o.icon} size={13} tint={on ? "light" : "dim"} />
                            )}
                            <div className={s.pvLabel}>{o.label}</div>
                        </button>
                    </Tip>
                );
            })}
        </div>
    );
};

function quickStats(summary: Summary) {
    return summary.stats.filter((x) => x.unit === "count" && x.value > 0).slice(0, 1);
}

/** First enabled step that still lacks its required markers (entrances), or null. */
export function missingMarkers(root: StepView | null): StepView | null {
    let miss: StepView | null = null;
    walk(root, (st) => {
        if (miss || !st.enabled) return;
        const need = st.markersRequired ?? 0;
        if (need <= 0) return;
        const have = st.markerList && st.markerList.length ? st.markerList.length : st.markers.reduce((n, m) => n + m.count, 0);
        if (have < need) miss = st;
    });
    return miss;
}

/** A step that still needs entrances: pressing the disabled primary starts placing one there. */
type EntranceLead = { path: string; kind: string; viaPrimary: boolean };

function leadToEntrances(state: ToolState, lead: EntranceLead) {
    if (lead.viaPrimary) act.primary(); // C# (and the mock) start placement on that step, back in Design
    else {
        if (state.stage !== "design") act.goToStage("design");
        if (lead.kind) {
            act.startMarkers(lead.path, lead.kind);
            act.setMarkerKind(lead.path, lead.kind);
        }
    }
    requestReveal("entrances:" + lead.path);
}

/** (s42) The "OK, permanent" tick beside Build: shown while permanent changes need an explicit OK. */
type AckChip = { on: boolean; set: (v: boolean) => void };

const Footer = ({
    state, summary, blockedReason, lead, compact, ack,
}: { state: ToolState; summary: Summary; blockedReason: string | null; lead?: EntranceLead | null; compact?: boolean; ack?: AckChip | null }) => {
    const { t, tl } = useT();
    const p = state.primary;
    const enabled = p.enabled && !blockedReason;
    const reason = blockedReason ?? (p.reason ? tl(p.reason) : "");
    const pending = summary.stale || state.planning;
    const ghostBusy = !!state.ghostPending && effectivePreviewMode(state) === "ghost";
    const busy = (pending || ghostBusy) && (state.stage === "design" || state.stage === "review");
    const showNumbers = state.stage === "design" && summary.ready && !compact;
    // Planning can take a moment: a clear spinner + label (not just a dot), stale numbers dimmed beside it.
    const progress = state.planning && typeof state.planningProgress === "number" ? Math.max(0, Math.min(1, state.planningProgress)) : null;
    const busyChip = busy ? (
        <div className={s.busy} data-testid="busy">
            <div className={s.spinner} />
            <div className={s.busyText}>
                {pending
                    ? progress != null
                        ? t("Placemaker.UI.PlanningPct", "Planning… {0}%", [Math.round(progress * 100)])
                        : t("Placemaker.UI.Planning", "Planning…")
                    : t("Placemaker.UI.PreviewUpdating", "Updating preview…")}
            </div>
            {pending && progress != null ? (
                <div className={s.busyTrack}>
                    <div className={s.busyFill} data-testid="busy-bar" style={{ width: Math.round(progress * 100) + "%" }} />
                </div>
            ) : null}
        </div>
    ) : null;
    const label = tl(p.label, t("Placemaker.UI.Primary.Next", "Next"));
    const buildOptions = (p.action === "build" || p.action === "finishEdit") && !compact;
    // s42: a blocked button says what blocks it ("Add an entrance to build"), short enough for one line.
    const shownLabel = !enabled && reason && reason.length <= 34 ? reason : label;
    const button = (
        <button
            type="button"
            data-testid="primary"
            data-action={p.action}
            aria-disabled={enabled ? "false" : "true"}
            className={classNames(s.primary, { [s.primaryOff]: !enabled, [s.primaryLead]: !enabled && !!lead, [s.primaryCancel]: p.action === "cancelBuild" })}
            onClick={(e) => {
                e.stopPropagation();
                if (enabled) act.primary();
                else if (lead && !state.markerMode) leadToEntrances(state, lead);
            }}
        >
            <div className={s.primaryLabel} data-testid={!enabled && reason ? "primary-reason" : undefined}>
                {shownLabel}
            </div>
            {enabled && p.action !== "cancelBuild" ? <div className={s.enterKey}>{t("Placemaker.UI.Key.Enter", "Enter")}</div> : null}
        </button>
    );
    let info: React.ReactNode = null;
    if (showNumbers) {
        info = (
            <div className={s.quickWrap}>
            {busyChip}
            <div className={classNames(s.quick, { [s.quickStale]: pending })} data-testid="quick-numbers" data-stale={pending ? "true" : "false"}>
                {busy ? null : (
                    <div className={s.quickItem}>
                        <Icon name="area" size={12} tint="dim" />
                        <div className={s.quickText}>{fmtArea(state.area.areaM2)}</div>
                    </div>
                )}
                {quickStats(summary).map((st) => (
                    <div key={st.key} className={s.quickItem}>
                        <Icon name={statIcon(st.key)} size={12} tint="dim" />
                        <div className={s.quickText}>{formatValue(st.value, "count") + " " + t(st.key, "").toLowerCase()}</div>
                    </div>
                ))}
                <div className={classNames(s.quickItem, { [s.quickBad]: !summary.canAfford })}>
                    <Icon name="cost" size={12} tint={summary.canAfford ? "dim" : "danger"} />
                    <div className={s.quickText}>{compactMoney(summary.cost)}</div>
                </div>
                <div className={s.quickItem}>
                    <Icon name="upkeep" size={12} tint="dim" />
                    <div className={s.quickText}>{compactMoney(summary.upkeep) + t("Placemaker.UI.PerMonth", "/mo")}</div>
                </div>
            </div>
            </div>
        );
    } else if (busyChip && !compact) {
        info = busyChip;
    }
    // s42: the Area stage has its own one-line summary; the footer only says what blocks the button.
    return (
        <div className={classNames(s.footer, { [s.footerCompact]: compact })}>
            {info ? <div className={s.footInfo}>{info}</div> : null}
            {buildOptions ? (
                // Review: the two switches for what Build does, side by side above the button row
                <div className={s.footSwitches} data-testid="build-options">
                    {state.grouping ? (
                        <Tip
                            title={t("Placemaker.UI.Build.Group", "Group as one asset")}
                            description={t(
                                "Placemaker.UI.Build.GroupTip",
                                "On: it is built as one asset. Click it to select all of it; the bulldozer removes all of it. Off: built as separate pieces you click, change and bulldoze one at a time. Removing all of it then takes Remove in a piece's info panel. You can ungroup or group it again later from there."
                            )}
                        >
                            <div className={s.footOption} data-testid="build-group-wrap" data-on={state.grouping.separate ? "false" : "true"}>
                                <Switch testId="build-group" on={!state.grouping.separate} onChange={(v) => act.setBuildUngrouped(!v)} />
                                <div className={s.footOptionLabel}>{t("Placemaker.UI.Build.Group", "Group as one asset")}</div>
                            </div>
                        </Tip>
                    ) : null}
                    {p.action === "build" ? (
                        <Tip
                            title={t("Placemaker.UI.Build.KeepArea", "Keep this area selected")}
                            description={t("Placemaker.UI.Build.KeepAreaTip", "On: after building, the area stays selected and Placemaker stays open, so you can build something else there. Off: Build closes Placemaker.")}
                        >
                            <div className={s.footOption} data-testid="build-keep-wrap" data-on={state.keepArea === false ? "false" : "true"}>
                                <Switch testId="build-keep" on={state.keepArea !== false} onChange={(v) => act.setKeepArea(v)} />
                                <div className={s.footOptionLabel}>{t("Placemaker.UI.Build.KeepArea", "Keep this area selected")}</div>
                            </div>
                        </Tip>
                    ) : null}
                </div>
            ) : null}
            <div className={s.primaryRow}>
                {ack && buildOptions ? (
                    <Tip title={t("Placemaker.UI.Review.AckShort", "OK, permanent")} description={t("Placemaker.UI.Review.AckTip", "Some changes stay after you remove the build (the amber tags). Click it to allow them.")}>
                        <div className={s.ackChip} data-testid="build-ack-wrap">
                            <Check testId="ack-permanent" checked={ack.on} onChange={ack.set} label={t("Placemaker.UI.Review.AckLong", "OK, some changes are permanent")} />
                        </div>
                    </Tip>
                ) : null}
                {p.action === "polish" && state.painter ? (
                    // Live polish: the selection polished again a moment after every change of the look or the selection
                    <Tip
                        title={t("Placemaker.UI.Polish.Live", "Live")}
                        description={t("Placemaker.UI.Polish.LiveTip", "On: the buildings in the selection are outgrown again a moment after every change you make, and the selection stays. Remove outgrow puts them back.")}
                    >
                        <div className={s.liveChip} data-testid="polish-live-wrap" data-on={state.painter.live ? "true" : "false"}>
                            <Switch testId="polish-live" on={!!state.painter.live} onChange={(v) => act.polishLive(v)} />
                            <div className={s.liveLabel}>{t("Placemaker.UI.Polish.Live", "Live")}</div>
                        </div>
                    </Tip>
                ) : null}
                {p.action === "polish" ? (
                    // (PROTOCOL 25) the painter's second button: the lots in the area back to as the game made them
                    <button
                        type="button"
                        data-testid="polish-no"
                        aria-disabled={enabled ? "false" : "true"}
                        className={classNames(s.primary, s.primarySecond, { [s.primaryOff]: !enabled })}
                        onClick={(e) => {
                            e.stopPropagation();
                            if (enabled) act.polishStamp(false);
                        }}
                    >
                        <div className={s.primaryLabel}>{t("Placemaker.UI.Painter.Unpolish", "Remove outgrow")}</div>
                    </button>
                ) : null}
                {!enabled && reason ? (
                    <Tip title={label} description={reason}>
                        {button}
                    </Tip>
                ) : (
                    button
                )}
            </div>
        </div>
    );
};

/**
 * One line of shortcut hints: the first (most relevant) up to three that fit whole — hints that would
 * be cut are dropped rather than clipped (measured after layout, works in Gameface too).
 */
const Hints = ({ state }: { state: ToolState }) => {
    const { tl } = useT();
    const ref = useRef<HTMLDivElement | null>(null);
    const list = state.hints.slice(0, 3);
    const sig = list.map((h) => h.text.key + (h.keys ?? []).join("+")).join("|");
    const [fit, setFit] = useState(3);
    useEffect(() => setFit(3), [sig]);
    useLayoutEffect(() => {
        const el = ref.current;
        if (el && fit > 1 && el.scrollWidth > el.clientWidth + 1) setFit(fit - 1);
    });
    if (!list.length) return null;
    const shown = list.slice(0, fit);
    return (
        <div className={s.hints} data-testid="hints" ref={ref}>
            {shown.map((h, i) => (
                <div key={i} className={classNames(s.hint, { [s.hintOnly]: shown.length === 1 })}>
                    {h.keys && h.keys.length ? <Keys keys={h.keys} /> : null}
                    <div className={s.hintText}>{tl(h.text)}</div>
                </div>
            ))}
        </div>
    );
};

/**
 * (s15) The game reloads Placemaker.mjs from disk but loads the DLL only at start: after an update the panel can
 * talk to an older (or newer) DLL until the game restarts. Then buttons for new things do nothing — say so.
 */
const VersionNotice = ({ state }: { state: ToolState }) => {
    const { t } = useT();
    if (!state.open || state.protocol === PROTOCOL) return null;
    return (
        <div className={s.versionNotice} data-testid="version-notice" data-protocol={state.protocol ?? "none"}>
            <Icon name="info" size={12} tint="warn" />
            <div className={s.versionText}>{t("Placemaker.UI.Version.Mismatch", "Restart the game: Placemaker's files were updated while it was running.")}</div>
        </div>
    );
};

// ───────────── panel ─────────────

export const PlacemakerPanel = () => {
    const state = useToolState();
    const catalog = useCatalog();
    const design = useDesign();
    const summary = useSummary();
    // (sketch study) Site plan style: the top view replaces the legend window (Design: beside the panel; Review: in it)
    const sitePlan = useSitePlan();
    const sitePlanBig = useSitePlanBig();
    const managed = useManaged();
    const rtl = useRtl();
    const { t, tl } = useT();
    const [picker, setPicker] = useState<PickerRequest | null>(null);
    // the layer over the panel that confirmation dialogs are drawn into (components/dialog)
    const [dialogLayer, setDialogLayer] = useState<HTMLDivElement | null>(null);
    // Each open is a fresh card (its hover preview ends when it is replaced).
    const pickerSerial = useRef(0);
    const [ack, setAck] = useState(false);
    // Outgrow's Global outgrow window (beside the panel; a look to edit asked for elsewhere opens it)
    // an outgrow type to edit asked for elsewhere (a zone's Edit in Zoning): the panel switches to Outgrow
    useLookEditorRequests(useCallback(() => act.polishCity("mode", "outgrow"), []));
    // never minimised any more (its button is gone): a panel saved minimised opens full
    useEffect(() => {
        if (state.panelCollapsed) act.setPanelCollapsed(false);
    }, [state.panelCollapsed]);
    const permKey = useMemo(() => JSON.stringify(summary.permanent), [summary.permanent]);
    const position = usePanelPosition();
    const layout = useMemo(() => layoutFor(position.width), [position.width]);

    useEffect(() => setAck(false), [permKey, state.stage === "review"]);
    useEffect(() => {
        if (!state.open || state.stage !== "design" || state.panelCollapsed) setPicker(null);
    }, [state.open, state.stage, state.panelCollapsed]);

    // Entering entrance placement while entrances are missing (button, or Enter in the game) brings the
    // Entrances section into view, open and flashing (FEEDBACK-4b).
    const placingPath = state.markerMode?.stepPath ?? "";
    const missingPath = state.open && state.stage === "design" ? missingMarkers(design.root)?.path ?? "" : "";
    useEffect(() => {
        if (placingPath && placingPath === missingPath) requestReveal("entrances:" + placingPath);
        // only when placement starts, not on every marker added
        // eslint-disable-next-line react-hooks/exhaustive-deps
    }, [placingPath]);

    const closePicker = useCallback(() => setPicker(null), []);
    const pickerCtx = useMemo(() => ({ open: (r: PickerRequest) => {
                pickerSerial.current++;
                setPicker(r);
            }, close: closePicker, current: picker }), [picker, closePicker]);

    if (!state.open) return null;

    // s33: ground paint alone (permanentNeedsAck false) is information, not a gate.
    const ackShown =
        state.stage === "review" &&
        summary.permanent.length > 0 &&
        summary.permanentNeedsAck !== false &&
        (state.primary.action === "build" || state.primary.action === "finishEdit");
    const needsAck = ackShown && !ack;
    const setAckBoth = (v: boolean) => {
        setAck(v);
        act.acknowledgePermanent(v);
    };

    const missing = state.stage === "design" || state.stage === "review" ? missingMarkers(design.root) : null;
    const gated = state.primary.action === "toReview" || state.primary.action === "build" || state.primary.action === "finishEdit";
    // Who to lead to when the player presses the (disabled) button while entrances are missing.
    const pr = state.primary;
    const lead: EntranceLead | null =
        pr.action === "placeMarkers" && !pr.enabled && pr.stepPath
            ? { path: pr.stepPath, kind: pr.markerKind ?? "", viaPrimary: true }
            : missing && gated
              ? { path: missing.path, kind: missing.markerKinds?.[0]?.kind ?? missing.markers[0]?.kind ?? "", viaPrimary: false }
              : null;
    let blockedReason: string | null = null;
    if (missing && gated) {
        const need = missing.markersRequired ?? 1;
        blockedReason =
            need > 1
                ? t("Placemaker.UI.Markers.NeedNBuild", "Add {0} entrances to build", [need])
                : t("Placemaker.UI.Markers.NeedOneBuild", "Add an entrance to build");
    } else if (needsAck) {
        blockedReason = t("Placemaker.UI.Review.AckFirst", "Click OK to build");
    }

    // the neighbourhood-polish tool (its own button, state.painter): Placemaker's panel and steps, its own Design and Review
    const polish = !!state.painter?.tool;
    // a Zoning type (or the old painter): the City window
    const cityAvailable = !!state.painter;
    // the panel's two modes: Placemaker (its stages) or Outgrow (the whole city's), by the switcher under the header
    const outgrow = cityAvailable && !!state.painter?.outgrowMode;
    let body: React.ReactNode = null;
    switch (state.stage) {
        case "area":
            body = <AreaStage state={state} />;
            break;
        case "design":
            body = polish ? <PolishDesign state={state} /> : <DesignStage catalog={catalog} design={design} state={state} />;
            break;
        case "review":
            body = (
<ReviewStage catalog={catalog} state={state} summary={summary} />
            );
            break;
        case "building":
            body = <BuildingStage state={state} />;
            break;
        case "manage":
            body = <ManageStage group={managed} />;
            break;
    }

    // Design is always Sketch; Review opens on Real and has the switch (the user: one mode per stage, room for the photo)
    const showPreviewToggle = !polish && !outgrow && state.stage === "review";
    // Header tips open UPWARDS (over the game's top bar), so they never sit on the stage bar below.
    const header = (
        <TipDirectionContext.Provider value="up">
            <div
                className={classNames(s.header, { [s.headerDragging]: position.dragging })}
                data-testid="panel-header"
                onMouseDown={position.onHeaderMouseDown}
                onDoubleClick={(e) => {
                    if (!isInsideButton(e.target as HTMLElement, e.currentTarget)) position.reset();
                }}
            >
                <Tip
                    title={t("Placemaker.UI.Move", "Move the panel")}
                    description={t("Placemaker.UI.MoveTip", "Drag to move. Double-click to dock.")}
                >
                    <div className={s.grip} data-testid="panel-grip">
                        <Icon name="grip" size={12} tint="dim" />
                    </div>
                </Tip>
                <Tip title={polish || outgrow ? t("Placemaker.UI.Polish.Name", "Outgrow") : t("Placemaker.Name", "Placemaker")}>
                    <div className={s.logo}>
                        <Icon name={polish || outgrow ? "shrub" : "logo"} size={16} tint="light" />
                    </div>
                </Tip>
                {/* the panel's two modes, in the title's place */}
                {cityAvailable ? (
                    <div className={s.headMode}>
                        <Segmented
                            testId="panel-mode"
                            value={outgrow ? "outgrow" : "placemaker"}
                            onChange={(v) => act.polishCity("mode", v)}
                            options={[
                                { value: "placemaker", label: t("Placemaker.Name", "Placemaker"), description: t("Placemaker.UI.ModePlacemakerTip", "Make roads, parks, car parks, shops and zones in an area you pick.") },
                                { value: "outgrow", label: t("Placemaker.UI.Polish.Name", "Outgrow"), description: t("Placemaker.UI.ModeOutgrowTip", "The whole city's outgrow: each zone type's default, the brush, the map lens, the outgrow types and their numbers.") },
                            ]}
                        />
                    </div>
                ) : (
                    <div className={s.title}>{t("Placemaker.Name", "Placemaker")}</div>
                )}
                {state.editingGroup != null && !outgrow ? <div className={s.editingPill}>{t("Placemaker.UI.Editing", "Editing")}</div> : null}
                {state.planning && !polish && !outgrow ? (
                    <Tip title={t("Placemaker.UI.Planning", "Planning…")}>
                        <div className={classNames(s.spinner, s.headSpinner)} data-testid="planning" />
                    </Tip>
                ) : null}
                <div className={s.flex} />
                {state.anarchy?.available ? (
                    <Btn
                        icon="anarchy"
                        iconSize={14}
                        selected={state.anarchy.enabled}
                        title={t("Placemaker.UI.Anarchy", "Anarchy")}
                        description={t("Placemaker.UI.AnarchyTip", "Ignore collisions (Anarchy mod).")}
                        onClick={() => act.setAnarchy(!state.anarchy?.enabled)}
                        testId="anarchy"
                    />
                ) : null}
                <div className={s.headSep} />
                <Btn
                    icon="close"
                    iconSize={14}
                    title={t("Placemaker.UI.Close", "Close")}
                    description={<Keys keys={["Esc"]} />}
                    onClick={act.close}
                    testId="close"
                />
            </div>
        </TipDirectionContext.Provider>
    );

    return (
        <PickerContext.Provider value={pickerCtx}>
            <TipDirectionContext.Provider value={position.side}>
                <PanelLayoutContext.Provider value={layout}>
                <DialogLayerContext.Provider value={dialogLayer}>
                <div className={classNames(s.dock, { [s.rtl]: rtl })} data-testid="placemaker-dock">
                    {picker ? <div className={s.backdrop} data-testid="picker-backdrop" onMouseDown={closePicker} /> : null}
                    <div
                        ref={position.wrapRef}
                        className={s.wrap}
                        style={position.style}
                        data-testid="placemaker-wrap"
                        data-docked={position.floating ? "false" : "true"}
                        data-width={position.width}
                        data-cols={layout.wide ? "2" : "1"}
                    >
                        <Tip
                                title={t("Placemaker.UI.Resize", "Resize the panel")}
                                description={t("Placemaker.UI.ResizeTip", "Drag to make it wider or narrower. Double-click for the default width.")}
                            >
                                <div
                                    className={classNames(s.resize, { [s.resizeActive]: position.resizing })}
                                    data-testid="panel-resize"
                                    onMouseDown={position.onResizeMouseDown}
                                    onDoubleClick={(e) => {
                                        e.stopPropagation();
                                        position.resetWidth();
                                    }}
                                />
                            </Tip>
                        <Tip
                                title={t("Placemaker.UI.ResizeHeight", "Resize the height")}
                                description={t("Placemaker.UI.ResizeHeightTip", "Drag to make it taller or shorter. Double-click for full height.")}
                            >
                                <div
                                    className={classNames(s.resizeBottom, { [s.resizeActive]: position.resizingHeight })}
                                    data-testid="panel-resize-height"
                                    onMouseDown={position.onResizeHeightMouseDown}
                                    onDoubleClick={(e) => {
                                        e.stopPropagation();
                                        position.resetHeight();
                                    }}
                                />
                            </Tip>
                        <div className={s.panel} data-testid="placemaker-panel" data-stage={state.stage}>
                            <div className={s.panelTint}>
                                {header}
                                {outgrow ? (
                                    <ErrorBoundary name="outgrow">
                                        <PolishCityCard state={state} />
                                    </ErrorBoundary>
                                ) : (
                                    <>
                                        {state.stage === "manage" ? null : (
                                            <ErrorBoundary name="stagebar">
                                                <StageBar state={state} />
                                            </ErrorBoundary>
                                        )}
                                        {showPreviewToggle ? (
                                            // under the stage bar, so Area / Design / Review never move when it appears
                                            <div className={s.subRow} data-testid="panel-preview-row">
                                                <PreviewToggle state={state} />
                                            </div>
                                        ) : null}
                                        <VersionNotice state={state} />
                                        <ScrollArea className={s.bodyWrap} testId="panel-body">
                                            <div className={s.body} key={state.stage}>
                                                <ErrorBoundary name={"stage-" + state.stage}>{body}</ErrorBoundary>
                                            </div>
                                        </ScrollArea>
                                        <ErrorBoundary name="footer">
                                            <Footer state={state} summary={summary} blockedReason={blockedReason} lead={lead} ack={ackShown ? { on: ack, set: setAckBoth } : null} />
                                        </ErrorBoundary>
                                        {state.stage === "design" || state.stage === "review" ? null : <Hints state={state} />}
                                    </>
                                )}
                            </div>
                        </div>
                        {!picker && !outgrow ? (
                            sitePlan && sitePlan.style !== "classic" && (state.stage === "design" || state.stage === "review") ? (
                                <ErrorBoundary name="siteplan">
                                    {sitePlanBig ? (
                                        <SitePlanFlyout sp={sitePlan} state={state} side={position.side === "left" ? "left" : "right"} />
                                    ) : state.stage === "design" ? (
                                        <SitePlanWindow sp={sitePlan} state={state} side={position.side === "left" ? "left" : "right"} />
                                    ) : null}
                                </ErrorBoundary>
                            ) : (
                                <ErrorBoundary name="legend">
                                    <Legend state={state} summary={summary} side={position.side === "left" ? "left" : "right"} />
                                </ErrorBoundary>
                            )
                        ) : null}
                        {picker ? (
                            <div className={classNames(s.flyout, position.side === "left" ? s.flyoutLeft : s.flyoutRight)} data-testid="picker-flyout">
                                <ErrorBoundary name="picker">
                                    <AssetPickerCard key={pickerSerial.current} req={picker} onClose={closePicker} />
                                </ErrorBoundary>
                            </div>
                        ) : null}
                        <div ref={setDialogLayer} className={s.dialogLayer} data-testid="dialog-layer" />
                        <AiLeaveGuard />
                    </div>
                </div>
                </DialogLayerContext.Provider>
                </PanelLayoutContext.Provider>
            </TipDirectionContext.Provider>
        </PickerContext.Provider>
    );
};

export const PlacemakerRoot = () => (
    <ErrorBoundary name="panel" fallback={(e, reset) => <PanelCrash error={e} reset={reset} />}>
        <PlacemakerPanel />
    </ErrorBoundary>
);

const PanelCrash = ({ error, reset }: { error: unknown; reset: () => void }) => (
    <div className={s.dock}>
        <div className={s.crash} onClick={reset} data-pm-crash="true">
            <strong>Placemaker</strong> — the panel hit an error and was stopped so the rest of the game UI keeps working. Click to try again.
            <div className={s.crashDetail}>{error instanceof Error ? `${error.name}: ${error.message}` : String(error)}</div>
        </div>
    </div>
);
