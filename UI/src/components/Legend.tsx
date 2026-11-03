// s22 — the map preview's legend: a small window at the bottom of the panel, on the side away from the screen edge.
// Lists the preview layers of the latest plan (summary.legend, C# Core/Build/PreviewLegend) with their swatch and
// one short number. Hover an entry → the map shows only that layer; click → hide / show it (kept for the game
// session); Reset → every layer at its default. Collapsible (remembered in this browser/game UI only).
// Replaces the per-step "On the map" road legend of s16 (roads are rows of the "Roads" group here).
import React, { useEffect, useState } from "react";
import classNames from "classnames";
import { LegendEntry, Summary, ToolState } from "../contract";
import { act } from "../lib/bindings";
import { humanize, TFunc, useT } from "../lib/l10n";
import { Tip } from "../lib/tips";
import { Btn, Icon } from "./common";
import { useAssetName } from "./picker";
import s from "./legend.module.scss";

const OPEN_KEY = "placemaker.legendOpen.v1";

function loadOpen(): boolean {
    try {
        return window.localStorage?.getItem(OPEN_KEY) !== "0";
    } catch {
        return true;
    }
}

function saveOpen(open: boolean) {
    try {
        window.localStorage?.setItem(OPEN_KEY, open ? "1" : "0");
    } catch {
        /* not remembered */
    }
}

/** "850 m" / "1.2 km". */
export function legendLength(m: number): string {
    return m >= 1000 ? (m / 1000).toFixed(1) + " km" : Math.round(m) + " m";
}

/** "350 m²" / "1.2 ha". */
export function legendArea(m2: number): string {
    return m2 >= 10000 ? (m2 / 10000).toFixed(1) + " ha" : Math.round(m2) + " m²";
}

const signed = (v: number) => (v > 0 ? "+" : v < 0 ? "−" : "") + Math.abs(v).toFixed(1);

/** One short number per row: terrain the cut / fill range, nets their length, ground layers their area, else a count. */
export function legendValue(e: LegendEntry): string {
    if (e.id === "terrain" && (e.minDelta || e.maxDelta)) {
        const parts: string[] = [];
        if (e.minDelta && e.minDelta < 0) parts.push(signed(e.minDelta));
        if (e.maxDelta && e.maxDelta > 0) parts.push(signed(e.maxDelta));
        return parts.join(" / ") + " m";
    }
    if (e.id === "issues") return e.blocked ? `${e.count} (${e.blocked} red)` : String(e.count);
    if (e.lengthM && e.lengthM > 0) return legendLength(e.lengthM);
    if ((e.id === "surfaces" || e.id === "water" || e.id.startsWith("area:")) && e.areaM2) return legendArea(e.areaM2);
    // Area overhaul: the fill under the cursor, the brush's fence and the reach are one thing each, no number.
    if (e.id === "area:fill" || e.id === "area:fence" || e.id === "area:reach") return "";
    return String(e.count);
}

export function legendVisible(e: LegendEntry, state: ToolState): boolean {
    const v = state.legendVisibility?.[e.id];
    return v !== undefined ? v : !e.defaultOff;
}

const Row = ({ e, state, t, hover, setHover, side }: { e: LegendEntry; state: ToolState; t: TFunc; hover: string; setHover: (id: string) => void; side: "left" | "right" }) => {
    const assetName = useAssetName();
    const visible = legendVisible(e, state);
    const isRoad = e.group === "roads";
    const role = isRoad ? e.id.replace(/^road:/, "") : e.id;
    const name = t(e.label, humanize(role));
    const road = isRoad ? e.assetName || (e.assetId ? assetName(role === "carFree" ? "net.pathway" : "net.road", e.assetId) : "") : "";
    return (
        <Tip
            title={name + (road ? " · " + road : "")}
            description={visible ? t("Placemaker.UI.Legend.RowTip", "Hover: show only this on the map. Click: hide it.") : t("Placemaker.UI.Legend.RowTipHidden", "Hidden on the map. Click to show it.")}
            direction={side}
        >
            <div
                className={classNames(s.row, { [s.rowHidden]: !visible, [s.rowHover]: hover === e.id, [s.rowRoad]: isRoad })}
                data-testid={`legend-row-${e.id}`}
                data-visible={visible ? "true" : "false"}
                onMouseEnter={() => {
                    setHover(e.id);
                    act.hoverLegend(e.id);
                }}
                onClick={(ev) => {
                    ev.stopPropagation();
                    act.setLegendVisible(e.id, !visible);
                }}
            >
                <div className={s.swatch}>
                    <div className={s.swatchPart} style={{ backgroundColor: e.color }} />
                    {e.color2 ? <div className={s.swatchPart} style={{ backgroundColor: e.color2 }} /> : null}
                </div>
                <div className={s.name}>{name}</div>
                <div className={s.detail}>{shortRoad(road) ? "· " + shortRoad(road) : ""}</div>
                <div className={s.value}>{legendValue(e)}</div>
                <Icon name={visible ? "eye" : "eyeOff"} size={12} className={s.eye} />
            </div>
        </Tip>
    );
};

/**
 * (s45) The road name in a legend row, short enough to never be cut: "Small Pedestrian Street" → "Small Ped.",
 * "Two-Lane Road" → "Two-Lane". Still too long → left out (the row tooltip has the full name).
 */
export function shortRoad(name: string): string {
    if (!name) return "";
    const short = name
        .replace(/\bPedestrian\b/g, "Ped.")
        .replace(/\s+(Road|Street|Avenue|Path)$/i, "")
        .trim();
    return short.length <= 14 ? short : "";
}

export const Legend = ({ state, summary, side }: { state: ToolState; summary: Summary; side: "left" | "right" }) => {
    const { t } = useT();
    const [open, setOpen] = useState(loadOpen);
    const [hover, setHover] = useState("");
    // Area overhaul: while choosing the area the window lists what the area tools draw (state.areaLegend).
    const entries = state.stage === "area" ? state.areaLegend ?? [] : summary.legend ?? [];
    const show = (state.stage === "area" || state.stage === "design" || state.stage === "review") && entries.length > 0;
    // A hover must never stick when the window goes away (stage change, collapse, plan without that layer).
    useEffect(() => {
        if ((!show || !open) && hover) {
            setHover("");
            act.hoverLegend("");
        }
    }, [show, open]);
    if (!show) return null;
    // Always clear on leave: the local hover can be stale after a click re-rendered the rows (the map kept isolating).
    const leave = () => {
        setHover("");
        act.hoverLegend("");
    };
    const changed = !!state.legendVisibility && Object.keys(state.legendVisibility).length > 0;
    const roads = entries.filter((e) => e.group === "roads");
    const firstRoad = roads.length ? roads[0].id : "";
    return (
        <div
            className={classNames(s.legend, side === "left" ? s.left : s.right, { [s.closed]: !open })}
            data-testid="legend-window"
            data-open={open ? "true" : "false"}
            onMouseLeave={leave}
        >
            <div
                className={s.header}
                data-testid="legend-toggle"
                onClick={() => {
                    saveOpen(!open);
                    setOpen(!open);
                }}
            >
                <Icon name="layers" size={13} />
                <div className={s.title}>{t("Placemaker.UI.Legend.Window", "On the map")}</div>
                <div className={s.flex} />
                {open && changed ? (
                    <Btn
                        icon="reset"
                        iconSize={12}
                        label={t("Placemaker.UI.Legend.All", "All")}
                        title={t("Placemaker.UI.Legend.AllTip", "Show every layer as it was")}
                        onClick={() => act.resetLegend()}
                        className={s.reset}
                        testId="legend-reset"
                    />
                ) : null}
                <Icon name={open ? "chevronDown" : "chevronUp"} size={12} />
            </div>
            {open ? (
                <div className={s.rows} data-testid="legend-rows">
                    {entries.map((e) => (
                        <React.Fragment key={e.id}>
                            {e.id === firstRoad ? <div className={s.group}>{t("Placemaker.UI.Legend.Roads", "Roads")}</div> : null}
                            <Row e={e} state={state} t={t} hover={hover} setHover={setHover} side={side} />
                        </React.Fragment>
                    ))}
                </div>
            ) : null}
        </div>
    );
};
