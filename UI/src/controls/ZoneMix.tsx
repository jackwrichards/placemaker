// The Zoning types' Zoning tool (Core/Layers/Types/Zoning/PolishLookLayer; the Design page's Zoning section) as one editor
// instead of a row per setting. It zones the empty land (the "Only edit existing" switch is gone: the user, 26 Oct, Outgrow's
// painter does that). Its two warnings (ZoningNotes) sit above the style card: it places the game's own zoning; Outgrow is separate.
//   * Zoning: a bar of the land (each zone's share, the rest empty) whose dividers are dragged; labels that do not fit sit
//     above or below it on a line to their part. One line per zone: its colour and icon, its type from a short menu (or an
//     exact zone of the game), its lots' width and depth and the road widths it goes on (two-handle sliders), remove.
//     "Mix in another zone", then how the lots mix.
// A zone's lots are picked on a small grid of cells (10 along the road, 6 deep, the road at the bottom; a cell is 8 m): the
// smallest lot solid, the biggest lighter; a click or drag moves the nearer corner.
// Every zone type has its own Outgrow on its line: a switch, its outgrow type (a dropdown) and Edit (Global outgrow's
// editor), kept by zone type (zoneOutgrow).
// It writes the same settings the rows would (zoneA/B/C, zoneAssetA/B/C, shareA/B/C, …), so styles, undo, locks and
// Randomize keep working.
import React, { ReactNode, useLayoutEffect, useRef, useState } from "react";
import classNames from "classnames";
import { LayerType, SettingValue, StepView } from "../contract";
import { act, playSound } from "../lib/bindings";
import { humanize, TFunc, useT } from "../lib/l10n";
import { ControlEx, valueOf } from "../lib/schema";
import { Icon, Switch } from "../components/common";
import { useAssetList, usePicker } from "../components/picker";
import z from "./zoneMix.module.scss";

const SLOTS = ["A", "B", "C"] as const;
type Slot = (typeof SLOTS)[number];
const MAX_ROAD = 48;
const GRID_W = 10, GRID_D = 6;

/** The zone tool's colours, by zone type. */
export const ZONE_COLOR: Record<string, string> = {
    residentialLow: "#9be07a",
    residentialRow: "#7fcf62",
    residentialMedium: "#5fb84a",
    residentialHigh: "#3f9a3a",
    mixed: "#52c7a6",
    lowRent: "#b9d35e",
    commercialLow: "#71b9ff",
    commercialHigh: "#3d86e6",
    officeLow: "#b896ff",
    officeHigh: "#8c66e6",
    industrial: "#f2b84b",
};

/** The game's zone of each type (Core/Layers/Common/ZoneKinds.PrefabId, without EU / NA). */
const KIND_ZONE: Record<string, string> = {
    residentialLow: "Residential Low",
    residentialRow: "Residential Medium Row",
    residentialMedium: "Residential Medium",
    residentialHigh: "Residential High",
    mixed: "Residential Mixed",
    lowRent: "Residential LowRent",
    commercialLow: "Commercial Low",
    commercialHigh: "Commercial High",
    officeLow: "Office Low",
    officeHigh: "Office High",
    industrial: "Industrial Manufacturing",
};

const KIND_GROUPS: { id: string; en: string; kinds: string[] }[] = [
    { id: "homes", en: "Homes", kinds: ["residentialLow", "residentialRow", "residentialMedium", "residentialHigh", "mixed", "lowRent"] },
    { id: "shops", en: "Shops", kinds: ["commercialLow", "commercialHigh"] },
    { id: "offices", en: "Offices", kinds: ["officeLow", "officeHigh"] },
    { id: "industry", en: "Industry", kinds: ["industrial"] },
];

/** A layer whose zoning this editor draws. */
export const isZoneMix = (layer: LayerType) => {
    const ids = new Set(layer.controls.map((c) => c.id));
    return ids.has("zoneArea") && ids.has("shareA") && ids.has("zoneA");
};

/** A zone's key: its name without EU / NA, lower case (NeighbourPolishSystem.ZoneKey): one Outgrow per zone type. */
export const zoneKey = (zone: string) => (zone || "").replace(/^(EU|NA)\s+/, "").trim().toLowerCase();

/** A zone type's Outgrow, as the Design page keeps it (zoneOutgrow): its switch, and the controls on its line (inline: the
 *  switch, the outgrow type's dropdown, Edit) and the dropdown's list under the line (pick). */
export type ZoneOutgrow = {
    isOn: (key: string) => boolean;
    setOn: (key: string, on: boolean) => void;
    inline: (key: string, id: string, open: boolean, toggle: () => void) => ReactNode;
    pick: (key: string, id: string, close: () => void) => ReactNode;
};

type Ctl = (id: string) => ControlEx | undefined;

function reader(step: StepView, layer: LayerType) {
    const ctl: Ctl = (id) => layer.controls.find((c) => c.id === id) as ControlEx | undefined;
    const val = (id: string): SettingValue | undefined => {
        const c = ctl(id);
        return c ? valueOf(step, c) : undefined;
    };
    const num = (id: string, d: number) => {
        const v = Number(val(id));
        return Number.isFinite(v) ? Math.round(v) : d;
    };
    const slots = SLOTS.map((sl, i) => ({
        sl,
        index: i,
        w0: num("widthMin" + sl, 2),
        w1: num("widthMax" + sl, 4),
        d0: num("depthMin" + sl, 2),
        d1: num("depthMax" + sl, 4),
        r0: num("roadMin" + sl, 0),
        r1: num("roadMax" + sl, MAX_ROAD),
        kind: String(val("zone" + sl) ?? ""),
        asset: typeof val("zoneAsset" + sl) === "string" ? (val("zoneAsset" + sl) as string) : "",
        share: Math.max(0, Number(val("share" + sl)) || 0),
    }));
    return { ctl, val, slots, used: slots.filter((x) => x.share > 0) };
}

const kindLabel = (t: TFunc, ctl: Ctl, kind: string) => {
    const o = (ctl("zoneA")?.options ?? []).find((x) => x.value === kind);
    return o ? t(o.label, humanize(o.value)) : humanize(kind);
};

/** A range of whole numbers on one track with two handles (the nearer handle follows the mouse). */
const RangeSlider = ({ lo, hi, min, max, testId, text, onCommit }: { lo: number; hi: number; min: number; max: number; testId: string; text: (a: number, b: number) => string; onCommit: (lo: number, hi: number) => void }) => {
    const [drag, setDrag] = useState<[number, number] | null>(null);
    const trackRef = useRef<HTMLDivElement>(null);
    const [a, b] = drag ?? [lo, hi];
    const pct = (v: number) => ((v - min) / (max - min || 1)) * 100;
    const fromX = (x: number) => {
        const r = trackRef.current?.getBoundingClientRect();
        if (!r || r.width <= 0) return a;
        const ratio = Math.min(1, Math.max(0, (x - r.left) / r.width));
        return Math.round(min + ratio * (max - min));
    };
    const onDown = (e: React.MouseEvent) => {
        if (e.button !== 0) return;
        e.preventDefault();
        e.stopPropagation();
        const v0 = fromX(e.clientX);
        const da = Math.abs(v0 - a), db = Math.abs(v0 - b);
        const which = da < db ? 0 : da > db ? 1 : v0 > b ? 1 : 0;
        let cur: [number, number] = [a, b];
        const apply = (v: number) => {
            cur = which === 0 ? [Math.min(v, cur[1]), cur[1]] : [cur[0], Math.max(v, cur[0])];
            setDrag(cur);
        };
        apply(v0);
        playSound("drag-slider");
        const move = (ev: MouseEvent) => apply(fromX(ev.clientX));
        const up = () => {
            window.removeEventListener("mousemove", move);
            window.removeEventListener("mouseup", up);
            setDrag(null);
            if (cur[0] !== lo || cur[1] !== hi) onCommit(cur[0], cur[1]);
        };
        window.addEventListener("mousemove", move);
        window.addEventListener("mouseup", up);
    };
    return (
        <div className={z.rangeWrap} data-testid={testId} data-lo={a} data-hi={b}>
            <div className={z.rangeTrack} ref={trackRef} onMouseDown={onDown}>
                <div className={z.rangeRail}>
                    <div className={z.rangeFill} style={{ left: pct(a) + "%", width: Math.max(0, pct(b) - pct(a)) + "%" }} />
                </div>
                <div className={z.rangeThumb} style={{ left: pct(a) + "%" }} />
                <div className={z.rangeThumb} style={{ left: pct(b) + "%" }} />
            </div>
            {text(a, b) ? <div className={z.rangeValue}>{text(a, b)}</div> : null}
        </div>
    );
};

/**
 * A zone's lots on a small grid of cells, the road along the bottom: the smallest lot (solid) and the biggest (lighter), both
 * from the road's corner. A click or drag moves the corner nearer to it; cells fade in and out as it moves.
 */
const LotGrid = ({ w0, w1, d0, d1, colour, testId, onCommit, onDrag }: { w0: number; w1: number; d0: number; d1: number; colour: string; testId: string; onCommit: (w0: number, w1: number, d0: number, d1: number) => void; onDrag: (v: [number, number, number, number] | null) => void }) => {
    const { t } = useT();
    const [drag, setDrag] = useState<[number, number, number, number] | null>(null);
    const gridRef = useRef<HTMLDivElement>(null);
    const [a0, a1, b0, b1] = drag ?? [Math.min(w0, GRID_W), Math.min(w1, GRID_W), Math.min(d0, GRID_D), Math.min(d1, GRID_D)];
    const cellAt = (clientX: number, clientY: number): [number, number] => {
        const r = gridRef.current?.getBoundingClientRect();
        if (!r || r.width <= 0) return [a1, b1];
        const x = Math.min(GRID_W, Math.max(1, Math.ceil(((clientX - r.left) / r.width) * GRID_W)));
        const y = Math.min(GRID_D, Math.max(1, Math.ceil(((r.bottom - clientY) / r.height) * GRID_D)));
        return [x, y];
    };
    const onDown = (e: React.MouseEvent) => {
        if (e.button !== 0) return;
        e.preventDefault();
        e.stopPropagation();
        const [x, y] = cellAt(e.clientX, e.clientY);
        // the nearer corner moves: the smallest lot's or the biggest lot's
        const dMin = Math.hypot(x - a0, y - b0), dMax = Math.hypot(x - a1, y - b1);
        const big = dMax < dMin || (dMax === dMin && (x > a1 || y > b1));
        let cur: [number, number, number, number] = [a0, a1, b0, b1];
        const apply = (px: number, py: number) => {
            cur = big ? [Math.min(cur[0], px), px, Math.min(cur[2], py), py] : [px, Math.max(cur[1], px), py, Math.max(cur[3], py)];
            setDrag(cur);
            onDrag(cur);
        };
        apply(x, y);
        playSound("drag-slider");
        const move = (ev: MouseEvent) => {
            const [mx, my] = cellAt(ev.clientX, ev.clientY);
            if (mx !== cur[big ? 1 : 0] || my !== cur[big ? 3 : 2]) apply(mx, my);
        };
        const up = () => {
            window.removeEventListener("mousemove", move);
            window.removeEventListener("mouseup", up);
            setDrag(null);
            onDrag(null);
            if (cur[0] !== w0 || cur[1] !== w1 || cur[2] !== d0 || cur[3] !== d1) onCommit(cur[0], cur[1], cur[2], cur[3]);
        };
        window.addEventListener("mousemove", move);
        window.addEventListener("mouseup", up);
    };
    const rowsOut: ReactNode[] = [];
    for (let y = GRID_D; y >= 1; y--) {
        const cells: ReactNode[] = [];
        for (let x = 1; x <= GRID_W; x++) {
            const inMin = x <= a0 && y <= b0, inMax = x <= a1 && y <= b1;
            cells.push(<div key={x} className={classNames(z.cell, { [z.cellMin]: inMin, [z.cellMax]: inMax && !inMin })} style={inMax ? { backgroundColor: colour } : undefined} />);
        }
        rowsOut.push(<div key={y} className={z.gridRow}>{cells}</div>);
    }
    return (
        <div
            className={z.lotGrid}
            data-testid={testId}
            data-w={a0 + "-" + a1}
            data-d={b0 + "-" + b1}
            title={t("Placemaker.UI.ZoneMix.GridTip", "Its lots, in zone cells (one cell is 8 m), the road along the bottom: the solid block is the smallest lot, the lighter one the biggest; each lot picks a size between. Click or drag near a corner to move it.")}
        >
            <div className={z.grid} ref={gridRef} onMouseDown={onDown}>
                {rowsOut}
            </div>
            <div className={z.legend}>
                <div className={z.legendSwatch} style={{ backgroundColor: colour }} />
                <div className={z.legendText}>{t("Placemaker.UI.ZoneMix.Min", "min")}</div>
                <div className={classNames(z.legendSwatch, z.legendLight)} style={{ backgroundColor: colour }} />
                <div className={z.legendText}>{t("Placemaker.UI.ZoneMix.Max", "max lot")}</div>
            </div>
        </div>
    );
};

type Part = { key: string; sl: Slot | null; share: number; colour: string; name: string };

/**
 * The land as one bar: each zone's share and the empty rest. A divider between two parts is dragged to move land between
 * them (dragging into the empty part zones more, away from it zones less). A label that does not fit its part sits above
 * or below the bar on a line to it.
 */
const ShareBar = ({ parts, onCommit, t }: { parts: Part[]; onCommit: (shares: Record<string, number>) => void; t: TFunc }) => {
    const barRef = useRef<HTMLDivElement>(null);
    const [width, setWidth] = useState(0);
    const [drag, setDrag] = useState<number[] | null>(null);
    // each label's width as drawn (hidden copies): a label goes in its part only when it really fits
    const measureRefs = useRef<Record<string, HTMLDivElement | null>>({});
    const [textW, setTextW] = useState<Record<string, number>>({});
    useLayoutEffect(() => {
        const w = barRef.current?.getBoundingClientRect().width ?? 0;
        if (Math.abs(w - width) > 1) setWidth(w);
        const next: Record<string, number> = {};
        let changed = false;
        for (const [k, el] of Object.entries(measureRefs.current)) {
            if (!el) continue;
            next[k] = el.getBoundingClientRect().width;
            if (Math.abs((textW[k] ?? -10) - next[k]) > 1) changed = true;
        }
        if (changed) setTextW(next);
    });
    const shares = drag ?? parts.map((p) => p.share);
    // the dividers: after every zone part (the last one too, so the empty part can be dragged out of a full bar)
    const zoneCount = parts.filter((p) => p.sl).length;
    const edges: number[] = [];
    let acc = 0;
    for (let i = 0; i < zoneCount; i++) {
        acc += shares[i];
        edges.push(acc);
    }
    const onDown = (e: React.MouseEvent, edge: number) => {
        if (e.button !== 0) return;
        e.preventDefault();
        e.stopPropagation();
        const r = barRef.current?.getBoundingClientRect();
        if (!r || r.width <= 0) return;
        const start = parts.map((p) => p.share);
        let cur = start.slice();
        const before = start.slice(0, edge).reduce((n, v) => n + v, 0);   // land left of the dragged part
        const right = edge + 1 < start.length ? start[edge + 1] : 0;       // the part to its right (a zone, or the empty rest)
        const apply = (clientX: number) => {
            const at = Math.round(Math.min(1, Math.max(0, (clientX - r.left) / r.width)) * 100);
            const lo = before + 1;                                     // the dragged part keeps 1 %
            const hi = before + start[edge] + right - (edge + 1 < zoneCount ? 1 : 0);
            const pos = Math.min(hi, Math.max(lo, at));
            cur = start.slice();
            cur[edge] = pos - before;
            if (edge + 1 < start.length) cur[edge + 1] = start[edge] + right - cur[edge];
            setDrag(cur);
        };
        apply(e.clientX);
        playSound("drag-slider");
        const move = (ev: MouseEvent) => apply(ev.clientX);
        const up = () => {
            window.removeEventListener("mousemove", move);
            window.removeEventListener("mouseup", up);
            setDrag(null);
            const out: Record<string, number> = {};
            parts.forEach((p, i) => {
                if (p.sl && cur[i] !== start[i]) out[p.sl] = cur[i];
            });
            if (Object.keys(out).length) onCommit(out);
        };
        window.addEventListener("mousemove", move);
        window.addEventListener("mouseup", up);
    };
    // labels: inside when they fit (measured), else above and below in turn
    let left = 0;
    let flip = 0;
    const labels = parts.map((p, i) => {
        const pctText = Math.round(shares[i]) + "%";
        const text = p.name + " " + pctText;
        const w = (shares[i] / 100) * width;
        const need = textW[p.key];
        const fits = need === undefined ? w > 120 : need + 6 <= w;
        const centre = left + shares[i] / 2;
        left += shares[i];
        const place = fits || shares[i] <= 0 ? "in" : flip++ % 2 === 0 ? "above" : "below";
        return { text, name: p.name, pct: pctText, centre, place, colour: p.colour, empty: !p.sl, key: p.key };
    });
    const above = labels.some((l) => l.place === "above");
    const below = labels.some((l) => l.place === "below");
    return (
        <div className={classNames(z.barBox, { [z.barBoxAbove]: above, [z.barBoxBelow]: below })} data-testid="zonemix-bar">
            {labels.map((l) => (
                <div key={"m" + l.key} className={classNames(z.barText, z.measure)} ref={(el) => (measureRefs.current[l.key] = el)}>
                    <span className={z.barName}>{l.name}</span>
                    <span className={z.barPct}>{l.pct}</span>
                </div>
            ))}
            <div className={z.bar} ref={barRef}>
                {parts.map((p, i) =>
                    shares[i] > 0 ? (
                        <div key={p.key} className={classNames(z.barPart, { [z.barEmpty]: !p.sl })} style={{ width: shares[i] + "%", backgroundColor: p.sl ? p.colour : undefined }} data-testid={"zonemix-part-" + p.key}>
                            {labels[i].place === "in" ? (
                                <div className={z.barText}>
                                    <span className={z.barName}>{labels[i].name}</span>
                                    <span className={z.barPct}>{labels[i].pct}</span>
                                </div>
                            ) : null}
                        </div>
                    ) : null,
                )}
                {edges.map((at, i) => (
                    <div key={"edge" + i} className={z.barEdge} style={{ left: Math.min(100, at) + "%" }} onMouseDown={(e) => onDown(e, i)} data-testid={"zonemix-edge-" + i} title={t("Placemaker.UI.ZoneMix.DragTip", "Drag to share the land")}>
                        <div className={z.barGrip} />
                    </div>
                ))}
            </div>
            {labels
                .filter((l) => l.place !== "in")
                .map((l) => (
                    <React.Fragment key={"lbl" + l.key}>
                        <div className={classNames(z.calloutLine, l.place === "above" ? z.calloutLineAbove : z.calloutLineBelow)} style={{ left: l.centre + "%" }} />
                        <div
                            className={classNames(z.callout, l.place === "above" ? z.calloutAbove : z.calloutBelow)}
                            style={l.centre > 75 ? { right: "0%" } : l.centre < 25 ? { left: "0%" } : { left: l.centre + "%", marginLeft: "-80rem", width: "160rem" }}
                        >
                            <div className={z.calloutText}>
                                <span className={z.barName}>{l.name}</span>
                                <span className={z.barPct}>{l.pct}</span>
                            </div>
                        </div>
                    </React.Fragment>
                ))}
        </div>
    );
};

/** A caption's words for a wrapping row: [underlined] parts kept whole, other text a word at a time (Chinese and Japanese a
 * character at a time, they have no spaces); gap: a space followed it. */
export const captionWords = (text: string) => {
    const out: { text: string; under: boolean; gap: boolean }[] = [];
    const piece = /\[([^\]]+)\]|([　-ヿ㐀-鿿＀-￯])|([^\s[　-ヿ㐀-鿿＀-￯]+)|(\s+)/g;
    for (let m = piece.exec(text); m; m = piece.exec(text)) {
        if (m[4]) {
            if (out.length) out[out.length - 1].gap = true;
        } else out.push({ text: m[1] ?? m[2] ?? m[3], under: !!m[1], gap: false });
    }
    return out;
};

/**
 * The Zoning types' warning, on the Design page between the Style row and the style card (the user, 26 Oct: players take
 * the Zoning tool for Outgrow): one orange caption, a warning icon on its first line: it places vanilla zoning, the same
 * as zoning by hand; Outgrow is a separate feature ("vanilla zoning" and "Outgrow" underlined, [marked] in the text).
 */
export const ZoningNotes = () => {
    const { t } = useT();
    // Gameface lays out every element as a box, so an underlined word cannot sit inside running text: the caption is a
    // wrapping row of words
    const words = captionWords(t("Placemaker.UI.ZoningCaption", "Places [vanilla zoning], same as zoning by hand. [Outgrow] is a separate feature."));
    return (
        <div className={z.warn} data-testid="zoning-note">
            <div className={z.warnIcon}>
                <Icon name="warning" size={13} tint="warn" />
            </div>
            <div className={z.warnText}>
                {words.map((w, i) => (
                    <div key={i} className={classNames(z.warnWord, { [z.warnUnder]: w.under, [z.warnGap]: w.gap })}>
                        {w.text}
                    </div>
                ))}
            </div>
        </div>
    );
};

export const ZoneMix = ({ step, layer, rows, outgrow }: { step: StepView; layer: LayerType; rows: (ids: string[]) => ReactNode; outgrow: ZoneOutgrow }) => {
    const { t } = useT();
    const picker = usePicker();
    const zones = useAssetList("zone");
    const [menu, setMenu] = useState<string | null>(null);   // "A" | "B" | "C"
    const { ctl, val, slots, used } = reader(step, layer);
    const set = (id: string, v: SettingValue) => act.setValue(step.path, id, v);
    const [picking, setPicking] = useState<string | null>(null);   // the zone key whose outgrow type list is open
    const [sizing, setSizing] = useState<Record<string, [number, number, number, number] | null>>({});

    /** The game's zone of a type (the city's EU or NA one when both exist). */
    const zoneOf = (kind: string, asset: string) => {
        if (asset) return zones?.find((a) => a.id === asset);
        const name = KIND_ZONE[kind];
        if (!name || !zones) return undefined;
        return zones.find((a) => a.id === name) ?? zones.find((a) => a.id.endsWith(" " + name));
    };
    const nameOf = (kind: string, asset: string) => (asset ? zoneOf(kind, asset)?.name || humanize(asset) : kindLabel(t, ctl, kind));
    const colourOf = (kind: string, asset: string) => (asset ? "#c9d3da" : ZONE_COLOR[kind] ?? "#c9d3da");

    // the bar: each zone's share of the land, the rest empty (shares above 100 together: shared out, nothing empty)
    const sum = used.reduce((n, x) => n + x.share, 0);
    const scale = sum > 100 ? 100 / sum : 1;
    const parts: Part[] = used.map((x) => ({ key: x.sl, sl: x.sl, share: Math.round(x.share * scale), colour: colourOf(x.kind, x.asset), name: nameOf(x.kind, x.asset) }));
    const zoned = parts.reduce((n, p) => n + p.share, 0);
    parts.push({ key: "empty", sl: null, share: Math.max(0, 100 - zoned), colour: "", name: t("Placemaker.UI.ZoneMix.Empty", "Empty") });

    const commitShares = (out: Record<string, number>) => {
        // a bar that was over 100 is written out as it is shown
        if (scale < 1) for (const p of parts) if (p.sl && !(p.sl in out)) out[p.sl] = p.share;
        for (const [sl, v] of Object.entries(out)) set("share" + sl, Math.max(1, Math.round(v)));
    };

    const addZone = () => {
        const free = slots.find((x) => x.share <= 0);
        if (!free) return;
        const usedKinds = new Set(used.map((x) => x.kind));
        if (usedKinds.has(free.kind)) {
            const kind = KIND_GROUPS.flatMap((g) => g.kinds).find((k) => !usedKinds.has(k));
            if (kind) set("zone" + free.sl, kind);
        }
        // its land: from the empty part when there is some, else a slice of the biggest zone
        const empty = Math.max(0, 100 - zoned);
        if (empty >= 10) set("share" + free.sl, Math.min(25, empty));
        else {
            const big = parts.filter((p) => p.sl).sort((p, q) => q.share - p.share)[0];
            const give = Math.max(1, Math.round(big.share / 3));
            set("share" + big.sl, big.share - give);
            set("share" + free.sl, give);
        }
        setMenu(free.sl);
    };

    const removeZone = (sl: Slot) => {
        if (used.length <= 1) return;
        set("share" + sl, 0);
        if (menu === sl) setMenu(null);
    };

    const pickKind = (sl: string, kind: string) => {
        set("zone" + sl, kind);
        if (slots.find((x) => x.sl === sl)?.asset) set("zoneAsset" + sl, "");
        setMenu(null);
    };

    const pickExact = (sl: Slot) => {
        const cur = slots.find((x) => x.sl === sl);
        picker.open({
            category: "zone",
            title: t("Placemaker.Polish.zoneAsset" + sl, "Exact zone"),
            mode: "single",
            selected: cur?.asset ? [cur.asset] : [],
            allowAutomatic: true,
            onPick: (ids) => set("zoneAsset" + sl, ids[0] ?? ""),
        });
        setMenu(null);
    };

    /** The zone's button: its colour, its icon, its name, a chevron; opens the menu under the line. */
    const ZoneButton = ({ id, kind, asset }: { id: string; kind: string; asset: string }) => {
        const zone = zoneOf(kind, asset);
        return (
            <button
                type="button"
                className={classNames(z.zoneButton, { [z.zoneButtonOn]: menu === id })}
                data-testid={"zonemix-pick-" + id}
                onClick={(e) => {
                    e.stopPropagation();
                    setMenu(menu === id ? null : id);
                }}
            >
                <div className={z.swatch} style={{ backgroundColor: colourOf(kind, asset) }} />
                <div className={z.zoneName}>{nameOf(kind, asset)}</div>
                {asset ? <div className={z.exactTag}>{t("Placemaker.UI.ZoneMix.Exact", "exact")}</div> : null}
                <Icon name="chevronDown" size={9} tint="dim" />
            </button>
        );
    };

    const Menu = ({ id, kind }: { id: string; kind: string }) => (
        <div className={z.menu} data-testid={"zonemix-menu-" + id}>
            {KIND_GROUPS.map((g) => (
                <div key={g.id} className={z.menuGroup}>
                    <div className={z.menuTitle}>{t("Placemaker.UI.ZoneMix.Group." + g.id, g.en)}</div>
                    <div className={z.menuChips}>
                        {g.kinds.map((k) => {
                            const zone = zoneOf(k, "");
                            return (
                                <button
                                    key={k}
                                    type="button"
                                    className={classNames(z.chip, { [z.chipOn]: k === kind })}
                                    data-testid={"zonemix-" + id + "-" + k}
                                    onClick={(e) => {
                                        e.stopPropagation();
                                        pickKind(id, k);
                                    }}
                                >
                                    <div className={z.swatch} style={{ backgroundColor: ZONE_COLOR[k] }} />
                                    {zone?.icon ? <Icon name={zone.icon} size={12} fallback="zone" /> : null}
                                    <div className={z.chipText}>{kindLabel(t, ctl, k)}</div>
                                </button>
                            );
                        })}
                    </div>
                </div>
            ))}
            <button
                type="button"
                className={z.exactButton}
                data-testid={"zonemix-" + id + "-exact"}
                onClick={(e) => {
                    e.stopPropagation();
                    pickExact(id as Slot);
                }}
            >
                <Icon name="search" size={10} tint="dim" />
                <div className={z.chipText}>{t("Placemaker.UI.ZoneMix.ExactPick", "Pick an exact zone (mods' too)…")}</div>
            </button>
        </div>
    );

    /** A zone type's Outgrow on its line: the switch, the outgrow type's dropdown and Edit. */
    const outgrowInline = (zkey: string, id: string) => outgrow.inline(zkey, id, picking === zkey, () => setPicking(picking === zkey ? null : zkey));
    const outgrowPick = (zkey: string, id: string) => (picking === zkey ? outgrow.pick(zkey, id, () => setPicking(null)) : null);

    const roadText = (a: number, b: number) =>
        a <= 0 && b >= MAX_ROAD ? t("Placemaker.UI.ZoneMix.AnyRoad", "any") : a <= 0 ? "≤ " + b + " m" : b >= MAX_ROAD ? "≥ " + a + " m" : a + "–" + b + " m";

    return (
        <div className={z.mix} data-testid="zonemix">
            <ShareBar parts={parts} onCommit={commitShares} t={t} />
            {used.map((x) => {
                const key = zoneKey(x.asset || KIND_ZONE[x.kind] || x.kind);
                return (
                    <div key={x.sl} className={z.zone} data-testid={"zonemix-zone-" + x.sl}>
                        <div className={z.line} data-testid={"zonemix-line-" + x.sl}>
                            <div className={z.leftCol}>
                                <div className={z.topRow}>
                                    <div className={z.zoneField}>
                                        <div className={z.fieldLabel}>{t("Placemaker.UI.ZoneMix.CapZone", "Zone")}</div>
                                        <ZoneButton id={x.sl} kind={x.kind} asset={x.asset} />
                                    </div>
                                    {outgrowInline(key, x.sl)}
                                </div>
                                <div
                                    className={z.roadField}
                                    title={t("Placemaker.UI.ZoneMix.RoadTip3", "Allowed road width: the roads this zone may line, by their width (two lanes 16 m, four lanes 24 m, six lanes 32 m). Its lots are only placed along roads in this range.")}
                                >
                                    <div className={z.fieldLabel}>{t("Placemaker.UI.ZoneMix.CapRoads2", "Allowed road width")}</div>
                                    <RangeSlider
                                        lo={x.r0}
                                        hi={x.r1}
                                        min={0}
                                        max={MAX_ROAD}
                                        testId={"zonemix-roads-" + x.sl}
                                        text={() => ""}
                                        onCommit={(a, b) => {
                                            if (a !== x.r0) set("roadMin" + x.sl, a);
                                            if (b !== x.r1) set("roadMax" + x.sl, b);
                                        }}
                                    />
                                    <div className={z.fieldValue}>{roadText(x.r0, x.r1)}</div>
                                </div>
                            </div>
                            <LotGrid
                                w0={x.w0}
                                w1={x.w1}
                                d0={x.d0}
                                d1={x.d1}
                                colour={colourOf(x.kind, x.asset)}
                                testId={"zonemix-grid-" + x.sl}
                                onDrag={(v) => setSizing({ ...sizing, [x.sl]: v })}
                                onCommit={(a0, a1, b0, b1) => {
                                    if (a0 !== x.w0) set("widthMin" + x.sl, a0);
                                    if (a1 !== x.w1) set("widthMax" + x.sl, a1);
                                    if (b0 !== x.d0) set("depthMin" + x.sl, b0);
                                    if (b1 !== x.d1) set("depthMax" + x.sl, b1);
                                }}
                            />
                            {used.length > 1 ? (
                                <button
                                    type="button"
                                    className={z.remove}
                                    data-testid={"zonemix-remove-" + x.sl}
                                    title={t("Placemaker.UI.ZoneMix.Remove", "Remove this zone")}
                                    onClick={(e) => {
                                        e.stopPropagation();
                                        removeZone(x.sl);
                                    }}
                                >
                                    <Icon name="close" size={9} tint="dim" />
                                </button>
                            ) : null}
                        </div>
                        {menu === x.sl ? <Menu id={x.sl} kind={x.kind} /> : null}
                        {outgrowPick(key, x.sl)}
                    </div>
                );
            })}
            {used.length < SLOTS.length ? (
                <button
                    type="button"
                    className={z.add}
                    data-testid="zonemix-add"
                    onClick={(e) => {
                        e.stopPropagation();
                        addZone();
                    }}
                >
                    <Icon name="plus" size={10} tint="accent" />
                    <div className={z.chipText}>{t("Placemaker.UI.ZoneMix.Add", "Mix in another zone")}</div>
                </button>
            ) : null}
            {rows(val("zonePattern") === "clumps" ? ["zonePattern", "zoneGrain"] : ["zonePattern"])}
            {rows(val("levelLots") === false ? ["levelLots"] : ["levelLots", "levelEarth", "levelBank", "levelWalls"])}
        </div>
    );
};
