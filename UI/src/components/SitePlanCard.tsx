// Sketch study — the top view: the plan drawn flat as what gets built (binding "sitePlan", C# Core/Build/SitePlan.cs), the
// same list the map's sketch draws (setting Sketch style: Site plan), so the picture, the map and this legend agree.
// The user (25 Oct): "a mini map view of a top-down view of that exact thing"; then: smaller, the legend in two columns, in
// Review in the same row as its legend, no notes on top of the picture, and hovering a row makes that thing blink rather
// than hiding everything else. The frame fits any outline (a T, a circle, a sliver): the picture keeps the shape's
// proportions and the room around it is the surrounding land and streets, dimmed, so nothing looks cut off or empty.
// Hover a row: it blinks here and on the map; click a row: hide it; the expand button (or a click on the picture): large.
import React, { useEffect, useMemo, useRef, useState } from "react";
import classNames from "classnames";
import { SitePlanRow, SitePlanStroke, SitePlanView, ToolState } from "../contract";
import { act } from "../lib/bindings";
import { useT } from "../lib/l10n";
import { Btn, Icon } from "./common";
import { legendLength } from "./Legend";
import s from "./sitePlan.module.scss";

const LAND = "#6f8a4c";
const ASPHALT = "#474b52";
const HIGHLIGHT = "#ffe14d";

// ───────────── the large view (Panel.tsx shows it beside the panel, where the legend window was) ─────────────

let bigOpen = false;
const bigListeners = new Set<() => void>();
export function setSitePlanBig(open: boolean) {
    bigOpen = open;
    bigListeners.forEach((l) => l());
}
export function useSitePlanBig(): boolean {
    const [, force] = useState(0);
    useEffect(() => {
        const l = () => force((n) => n + 1);
        bigListeners.add(l);
        return () => {
            bigListeners.delete(l);
        };
    }, []);
    return bigOpen;
}

// ───────────── helpers ─────────────

/** An opaque colour: hex at alpha over the land (overlapping pieces never double up; Gameface draws opaque fills fastest). */
function over(hex: string, alpha: number, base = LAND): string {
    const p = (h: string) => [1, 3, 5].map((i) => parseInt(h.substr(i, 2), 16));
    const a = p(hex), b = p(base);
    if (a.some(isNaN) || b.some(isNaN)) return hex;
    const m = a.map((v, i) => Math.round(v * alpha + b[i] * (1 - alpha)));
    return "#" + m.map((v) => v.toString(16).padStart(2, "0")).join("");
}

const f1 = (v: number) => (Math.round(v * 10) / 10).toString();
/** World (x, z) → picture (x, −z): north up, as the game's top view. */
const P = (x: number, z: number) => f1(x) + "," + f1(-z);

function ringD(flat: number[]): string {
    if (flat.length < 6) return "";
    let d = "M" + P(flat[0], flat[1]);
    for (let i = 2; i + 1 < flat.length; i += 2) d += "L" + P(flat[i], flat[i + 1]);
    return d + "Z";
}

function lineD(flat: number[]): string {
    if (flat.length < 4) return "";
    let d = "M" + P(flat[0], flat[1]);
    for (let i = 2; i + 1 < flat.length; i += 2) d += "L" + P(flat[i], flat[i + 1]);
    return d;
}

function circlesD(xyr: number[], rOf: (r: number) => number, dx = 0, dz = 0): string {
    let d = "";
    for (let i = 0; i + 2 < xyr.length; i += 3) {
        const r = rOf(xyr[i + 2]);
        const x = xyr[i] + dx * xyr[i + 2], z = xyr[i + 1] + dz * xyr[i + 2];
        d += "M" + f1(x - r) + "," + f1(-z) + "a" + f1(r) + "," + f1(r) + " 0 1,0 " + f1(2 * r) + ",0a" + f1(r) + "," + f1(r) + " 0 1,0 " + f1(-2 * r) + ",0";
    }
    return d;
}

function dotsD(xz: number[], r: number): string {
    const xyr: number[] = [];
    for (let i = 0; i + 1 < xz.length; i += 2) xyr.push(xz[i], xz[i + 1], r);
    return circlesD(xyr, (v) => v);
}

/** Strokes grouped by width (one path each: a junction of equal roads is one surface). */
function byWidth(list: SitePlanStroke[], minW: number): { w: number; d: string }[] {
    const m = new Map<number, string>();
    for (const st of list) {
        const w = Math.max(minW, Math.round(st.w * 2) / 2);
        m.set(w, (m.get(w) ?? "") + lineD(st.p));
    }
    return Array.from(m.entries()).map(([w, d]) => ({ w, d }));
}

export function rowValue(r: SitePlanRow): string {
    if (r.unit === "m") return legendLength(r.count);
    if (r.unit === "delta") return r.count ? "±" + r.count.toFixed(1) + " m" : "";
    if (r.unit === "" || !r.count) return "";
    return String(r.count);
}

// ───────────── the picture ─────────────

/** The view box: the outline's bounds plus a margin, widened to the box's proportions (the extra is surrounding land). */
function frame(sp: SitePlanView, w: number, h: number) {
    const minX = sp.min[0], minZ = sp.min[1], maxX = sp.max[0], maxZ = sp.max[1];
    const span = Math.max(maxX - minX, maxZ - minZ, 10);
    const pad = Math.max(6, span * 0.06);
    let vw = maxX - minX + 2 * pad, vh = maxZ - minZ + 2 * pad;
    const want = w / Math.max(1, h);
    if (vw / vh < want) vw = vh * want;
    else vh = vw / want;
    const cx = (minX + maxX) / 2, cz = (minZ + maxZ) / 2;
    return { vx: cx - vw / 2, vy: -cz - vh / 2, vw, vh, pxPerM: w / vw };
}

type Layer = { row: string; el: React.ReactElement };

/** Every drawn layer with its legend row (the base picture and the hover highlight both come from it). */
function layers(sp: SitePlanView, pxPerM: number): Layer[] {
    const px = (n: number) => n / pxPerM;   // n screen pixels in metres
    const out: Layer[] = [];
    const add = (row: string, el: React.ReactElement) => out.push({ row, el });
    for (const f of sp.fills) add(f.row, <path d={f.rings.map(ringD).join("")} fill={over(f.color, f.alpha)} />);
    if (sp.plots.length) {
        const byColor = new Map<string, string>();
        sp.plots.forEach((p) => byColor.set(p.color, (byColor.get(p.color) ?? "") + ringD(p.q)));
        byColor.forEach((d, c) => add("cutouts", <path d={d} fill={over(c, 0.55)} />));
        add("cutouts", <path d={sp.plots.map((p) => ringD(p.q)).join("")} fill="none" stroke="#ffffff" strokeOpacity={0.5} strokeWidth={Math.max(0.25, px(0.6))} />);
    }
    for (const hb of sp.heat ?? []) add("terrain", <path d={hb.rings.map(ringD).join("")} fill={hb.color} fillOpacity={hb.alpha} />);
    const roads = sp.cars.filter((c) => c.mark), aisles = sp.cars.filter((c) => !c.mark);
    const asphalt = over(ASPHALT, 0.92);
    byWidth(aisles, px(1.5)).forEach((g) => add("parking", <path d={g.d} stroke={asphalt} strokeWidth={g.w} fill="none" strokeLinecap="round" strokeLinejoin="round" />));
    byWidth(roads, px(1.5)).forEach((g) => add("roads", <path d={g.d} stroke={asphalt} strokeWidth={g.w} fill="none" strokeLinecap="round" strokeLinejoin="round" />));
    if (sp.caps.length) add("roads", <path d={circlesD(sp.caps, (r) => r)} fill={asphalt} />);
    if (roads.length && pxPerM > 0.6) add("roads", <path d={roads.map((r) => lineD(r.p)).join("")} stroke="#f3f1e8" strokeOpacity={0.7} strokeWidth={Math.max(0.3, px(0.6))} strokeDasharray="3 3" fill="none" />);
    byWidth(sp.walks, px(1.2)).forEach((g) => add("paths", <path d={g.d} stroke="#eadfc2" strokeWidth={g.w} fill="none" strokeLinecap="round" strokeLinejoin="round" />));
    if (sp.fences.length) add("objects", <path d={sp.fences.map(lineD).join("")} stroke="#6b5440" strokeWidth={Math.max(0.35, px(0.8))} fill="none" />);
    if (sp.stalls.length && pxPerM * 2.5 > 3) {
        let d = "";
        for (let i = 0; i + 3 < sp.stalls.length; i += 4) d += "M" + P(sp.stalls[i], sp.stalls[i + 1]) + "L" + P(sp.stalls[i + 2], sp.stalls[i + 3]);
        add("parking", <path d={d} stroke="#ffffff" strokeOpacity={0.9} strokeWidth={Math.max(0.18, px(0.7))} fill="none" />);
    }
    if (sp.chargers.length) add("parking", <path d={dotsD(sp.chargers, Math.max(0.7, px(1.2)))} fill="#3fd28a" />);
    if (sp.places.length) add("features", <path d={sp.places.map(ringD).join("")} fill="#f0a868" stroke="#a95f24" strokeWidth={Math.max(0.25, px(0.7))} />);
    if (sp.benches.length) add("objects", <path d={sp.benches.map(ringD).join("")} fill="#7a5a3a" />);
    if (sp.bins.length) add("objects", <path d={dotsD(sp.bins, Math.max(0.35, px(0.8)))} fill="#26272a" />);
    if (sp.lamps.length) add("objects", <path d={dotsD(sp.lamps, Math.max(0.45, px(1)))} fill="#fff3c4" stroke="#333333" strokeWidth={Math.max(0.12, px(0.4))} />);
    if (sp.bushes.length) add("planting", <path d={circlesD(sp.bushes, (r) => Math.max(r, px(1)))} fill={over("#5f9e48", 0.9)} />);
    if (sp.trees.length) {
        const rr = (r: number) => Math.max(r, px(1.6));
        add("trees", <path d={circlesD(sp.trees, rr, 0.25, -0.3)} fill="#08140a" fillOpacity={0.28} />);
        add("trees", <path d={circlesD(sp.trees, rr)} fill="#3b7533" />);
        add("trees", <path d={circlesD(sp.trees, (r) => rr(r) * 0.55, -0.22, 0.22)} fill="#5a9a44" />);
    }
    const badge = px(6);
    for (let i = 0; i + 2 < sp.entrances.length; i += 3) {
        const x = sp.entrances[i], z = sp.entrances[i + 1], a = sp.entrances[i + 2];
        const ux = Math.cos(a), uz = Math.sin(a);
        const tip = P(x + ux * badge * 0.45, z + uz * badge * 0.45);
        const l = P(x - ux * badge * 0.25 - uz * badge * 0.4, z - uz * badge * 0.25 + ux * badge * 0.4);
        const r = P(x - ux * badge * 0.25 + uz * badge * 0.4, z - uz * badge * 0.25 - ux * badge * 0.4);
        add("entrances", <path d={circlesD([x, z, badge], (v) => v)} fill="#ffffff" stroke="#0b1a2a" strokeOpacity={0.4} strokeWidth={px(1)} />);
        add("entrances", <path d={"M" + l + "L" + tip + "L" + r} fill="none" stroke="#4fa3ff" strokeWidth={px(2)} strokeLinecap="round" strokeLinejoin="round" />);
    }
    if (sp.issues.length) {
        const xyr: number[] = [];
        for (let i = 0; i + 2 < sp.issues.length; i += 3) xyr.push(sp.issues[i], sp.issues[i + 1], badge);
        add("issues", <path d={circlesD(xyr, (v) => v)} fill="#e5322d" stroke="#ffffff" strokeWidth={px(1.2)} />);
    }
    return out;
}

/** The same shape in the highlight colour (fills filled, lines stroked a little wider). */
function highlighted(el: React.ReactElement, pxPerM: number): React.ReactElement {
    const p = el.props as any;
    const stroked = p.fill === "none";
    return React.cloneElement(el, {
        // a big fill (a car park's asphalt, a lawn) only tinted, so the picture under it still reads
        fill: stroked ? "none" : HIGHLIGHT,
        fillOpacity: stroked ? undefined : 0.4,
        stroke: HIGHLIGHT,
        // wide strokes (a road, an aisle) half strength: a car park's aisles cover most of it
        strokeOpacity: stroked && (Number(p.strokeWidth) || 0) * pxPerM > 3 ? 0.5 : 1,
        strokeWidth: stroked ? (Number(p.strokeWidth) || 1) + 2 / pxPerM : 2 / pxPerM,
        strokeDasharray: undefined,
    });
}

interface PicProps {
    sp: SitePlanView;
    /** Box size in px (the picture is fitted to it). */
    w: number;
    h: number;
    hover: string;
    hidden: (id: string) => boolean;
    hiddenKey: string;
}

export const SitePlanPicture = ({ sp, w, h, hover, hidden, hiddenKey }: PicProps) => {
    const fr = frame(sp, w, h);
    const all = useMemo(() => layers(sp, fr.pxPerM), [sp.stamp, w, h]);
    // the base picture is built once per plan, size and hidden set; a hover only adds the blinking copy on top
    const base = useMemo(() => {
        const px = (n: number) => n / fr.pxPerM;
        const outline = ringD(sp.outline);
        const els: React.ReactNode[] = [];
        let k = 0;
        const streets = byWidth(sp.streets, px(2));
        streets.forEach((g) => els.push(<path key={k++} d={g.d} stroke="#c9ccd0" strokeWidth={g.w + 0.8} fill="none" strokeLinecap="round" strokeLinejoin="round" />));
        streets.forEach((g) => els.push(<path key={k++} d={g.d} stroke="#8d9196" strokeWidth={Math.max(px(1.5), g.w - 3)} fill="none" strokeLinecap="round" strokeLinejoin="round" />));
        const outer = "M" + f1(fr.vx - 10) + "," + f1(fr.vy - 10) + "h" + f1(fr.vw + 20) + "v" + f1(fr.vh + 20) + "h" + f1(-fr.vw - 20) + "Z";
        els.push(<path key={k++} d={outer + outline} fill="#0b121a" fillOpacity={0.34} fillRule="evenodd" />);
        els.push(<path key={k++} d={outline} fill={over("#9cc76f", 0.28)} />);
        for (const l of all) if (!hidden(l.row)) els.push(React.cloneElement(l.el, { key: k++ }));
        els.push(<path key={k++} d={outline} fill="none" stroke="#0b1a2a" strokeOpacity={0.4} strokeWidth={px(3)} strokeLinejoin="round" />);
        els.push(<path key={k++} d={outline} fill="none" stroke="#d6ecff" strokeWidth={px(1.3)} strokeLinejoin="round" />);
        return els;
    }, [all, hiddenKey]);
    const vb = [fr.vx, fr.vy, fr.vw, fr.vh].map(f1).join(" ");
    const lit = hover && !hidden(hover) ? all.filter((l) => l.row === hover) : [];
    const bar = fr.vw > 600 ? 100 : fr.vw > 250 ? 50 : fr.vw > 100 ? 20 : 10;
    return (
        <>
            {/* a new svg element per plan: the game's view does not repaint paths whose points change */}
            <svg key={sp.stamp + "|" + w + "|" + h + "|" + hiddenKey} width="100%" height="100%" viewBox={vb} preserveAspectRatio="none">
                <rect x={fr.vx} y={fr.vy} width={fr.vw} height={fr.vh} fill={LAND} />
                {base}
            </svg>
            {lit.length ? (
                <div className={s.blink}>
                    <svg key={"hi|" + hover + "|" + sp.stamp + "|" + w} width="100%" height="100%" viewBox={vb} preserveAspectRatio="none">
                        {lit.map((l, i) => React.cloneElement(highlighted(l.el, fr.pxPerM), { key: i }))}
                    </svg>
                </div>
            ) : null}
            <div className={s.scale}>
                <div className={s.scaleBar} style={{ width: Math.round(bar * fr.pxPerM) + "px" }} />
                <div className={s.scaleText}>{bar} m</div>
            </div>
        </>
    );
};

// ───────────── legend (the swatch is the map's own shape) ─────────────

export const Swatch = ({ sym }: { sym: string }) => {
    const g = (inner: React.ReactNode) => (
        <svg width="100%" height="100%" viewBox="0 0 30 18">
            <rect width="30" height="18" rx="3" fill={LAND} />
            {inner}
        </svg>
    );
    switch (sym) {
        case "road":
            return g(<>
                <rect x="0" y="5" width="30" height="8" fill={over(ASPHALT, 0.92)} />
                <path d="M1,9L29,9" stroke="#f3f1e8" strokeWidth="0.8" strokeDasharray="3 3" />
            </>);
        case "bays":
            return g(<>
                <rect width="30" height="18" rx="3" fill={over(ASPHALT, 0.9)} />
                <path d="M5,3L5,11M11,3L11,11M17,3L17,11M23,3L23,11M5,3L23,3" stroke="#ffffff" strokeWidth="0.9" fill="none" />
            </>);
        case "path":
            return g(<path d="M3,13C11,3 19,15 27,5" stroke="#eadfc2" strokeWidth="3.4" fill="none" strokeLinecap="round" />);
        case "grass":
            return g(<rect x="3" y="3" width="24" height="12" rx="2" fill={over("#8fd16a", 0.55)} />);
        case "paving":
            return g(<rect x="3" y="3" width="24" height="12" rx="2" fill={over("#e6d8b8", 0.75)} />);
        case "water":
            return g(<rect x="3" y="3" width="24" height="12" rx="5" fill={over("#3f86d8", 0.8)} />);
        case "zone":
            return g(<path d="M5,4h6v10h-6ZM12,4h6v10h-6ZM19,4h6v10h-6Z" fill={over("#4f8fe6", 0.65)} />);
        case "terrain":
            return g(<>
                <rect x="3" y="3" width="12" height="12" fill="#ff5c2e" fillOpacity="0.8" />
                <rect x="15" y="3" width="12" height="12" fill="#2f7fff" fillOpacity="0.8" />
            </>);
        case "tree":
            return g(<>
                <circle cx="16" cy="10" r="6" fill="#08140a" fillOpacity="0.3" />
                <circle cx="15" cy="9" r="6" fill="#3b7533" />
                <circle cx="13.6" cy="7.6" r="3.3" fill="#5a9a44" />
            </>);
        case "bush":
            return g(<path d={circlesD([9, -9, 2.3, 15, -10, 1.9, 21, -8, 2.1], (r) => r)} fill={over("#5f9e48", 0.9)} />);
        case "place":
            return g(<rect x="9" y="4" width="12" height="10" fill="#f0a868" stroke="#a95f24" strokeWidth="0.9" />);
        case "furniture":
            return g(<>
                <circle cx="8" cy="9" r="1.6" fill="#fff3c4" stroke="#333333" strokeWidth="0.4" />
                <rect x="12.5" y="8" width="5" height="2" fill="#7a5a3a" />
                <circle cx="22" cy="9" r="1.3" fill="#26272a" />
            </>);
        case "entrance":
            return g(<>
                <circle cx="15" cy="9" r="6.2" fill="#ffffff" />
                <path d="M12.8,6L17.4,9L12.8,12" fill="none" stroke="#4fa3ff" strokeWidth="1.7" strokeLinecap="round" strokeLinejoin="round" />
            </>);
        case "issue":
            return g(<circle cx="15" cy="9" r="6.2" fill="#e5322d" stroke="#ffffff" strokeWidth="1" />);
        default:
            return g(null);
    }
};

export function rowVisible(id: string, state: ToolState): boolean {
    const v = state.legendVisibility?.[id];
    return v !== undefined ? v : true;
}

/** The legend in columns (2, or 3 in the large view): hover = blink, click = hide / show. */
const Rows = ({ sp, state, hover, setHover, columns }: { sp: SitePlanView; state: ToolState; hover: string; setHover: (id: string) => void; columns: 2 | 3 }) => {
    const { t } = useT();
    return (
        <div className={s.rows} data-testid="siteplan-rows" onMouseLeave={() => { setHover(""); act.hoverLegend(""); }}>
            {sp.legend.map((r) => {
                const visible = rowVisible(r.id, state);
                return (
                    <div
                        key={r.id}
                        className={classNames(s.row, columns === 3 ? s.row3 : s.row2, { [s.rowHidden]: !visible, [s.rowHover]: hover === r.id })}
                        data-testid={"siteplan-row-" + r.id}
                        data-visible={visible ? "true" : "false"}
                        onMouseEnter={() => { setHover(r.id); act.hoverLegend(r.id); }}
                        onClick={(e) => { e.stopPropagation(); act.setLegendVisible(r.id, !visible); }}
                    >
                        <div className={s.swatch}><Swatch sym={r.sym} /></div>
                        <div className={s.name}>{t(r.label.key, r.en)}</div>
                        <div className={s.value}>{rowValue(r)}</div>
                    </div>
                );
            })}
        </div>
    );
};

// ───────────── the card ─────────────

/** The width of a box in px, measured once and on resize (the panel is resizable; no ResizeObserver in the game's view). */
function useBoxWidth(ref: React.RefObject<HTMLDivElement>): number {
    const [w, setW] = useState(0);
    useEffect(() => {
        const measure = () => {
            const el = ref.current;
            if (el) setW((old) => (Math.abs(el.offsetWidth - old) > 1 ? el.offsetWidth : old));
        };
        measure();
        window.addEventListener("resize", measure);
        const id = window.setInterval(measure, 700);
        return () => {
            window.removeEventListener("resize", measure);
            window.clearInterval(id);
        };
    }, []);
    return w;
}

/** The picture's height for a width: the shape's proportions, kept between a wide strip and a near square. */
function ratioOf(sp: SitePlanView, maxRatio: number): number {
    const ratio = (sp.max[1] - sp.min[1] + 16) / Math.max(1, sp.max[0] - sp.min[0] + 16);
    return Math.min(maxRatio, Math.max(0.6, ratio));
}

const hiddenKeyOf = (state: ToolState) =>
    Object.entries(state.legendVisibility ?? {}).filter(([, v]) => !v).map(([k]) => k).sort().join(",");

/**
 * The top view. "row" (Review): the picture beside its legend; "stack" (the Design window): the picture over its legend.
 */
export const SitePlanCard = ({ sp, state, layout }: { sp: SitePlanView; state: ToolState; layout: "row" | "stack" }) => {
    const { t } = useT();
    const ref = useRef<HTMLDivElement>(null);
    const w = useBoxWidth(ref);
    const [hover, setHover] = useState("");
    const big = useSitePlanBig();
    const hidden = (id: string) => !rowVisible(id, state);
    const ratio = ratioOf(sp, layout === "row" ? 1 : 0.75);
    const h = w > 0 ? Math.round(w * ratio) : 120;
    return (
        <div className={classNames(s.card, layout === "row" ? s.cardRow : s.cardStack, { [s.stale]: sp.stale })} data-testid="siteplan-card">
            <div ref={ref} className={classNames(s.pic, layout === "row" ? s.picRow : null)} style={{ height: h + "px" }} data-testid="siteplan-picture" onClick={() => setSitePlanBig(!big)}>
                {w > 0 ? <SitePlanPicture sp={sp} w={w} h={h} hover={hover} hidden={hidden} hiddenKey={hiddenKeyOf(state)} /> : null}
                <div className={s.expand} onClick={(e) => e.stopPropagation()}>
                    <Btn icon="expand" iconSize={11} title={t("Placemaker.UI.SitePlan.Expand", "Show the top view large")} onClick={() => setSitePlanBig(!big)} testId="siteplan-expand" />
                </div>
            </div>
            <div className={layout === "row" ? s.legendRow : s.legendStack}>
                <Rows sp={sp} state={state} hover={hover} setHover={setHover} columns={2} />
            </div>
        </div>
    );
};

/** Design: the top view in a small window beside the panel (where the legend window was), updating as you change things. */
export const SitePlanWindow = ({ sp, state, side }: { sp: SitePlanView; state: ToolState; side: "left" | "right" }) => {
    const { t } = useT();
    return (
        <div className={classNames(s.window, side === "left" ? s.left : s.right)} data-testid="siteplan-window">
            <div className={s.flyHead}>
                <Icon name="compass" size={12} />
                <div className={s.flyTitle}>{t("Placemaker.UI.SitePlan.Title", "Top view")}</div>
            </div>
            <SitePlanCard sp={sp} state={state} layout="stack" />
        </div>
    );
};

/** The large view beside the panel. */
export const SitePlanFlyout = ({ sp, state, side }: { sp: SitePlanView; state: ToolState; side: "left" | "right" }) => {
    const { t } = useT();
    const [hover, setHover] = useState("");
    const hidden = (id: string) => !rowVisible(id, state);
    const ref = useRef<HTMLDivElement>(null);
    const w = useBoxWidth(ref);
    const ratio = ratioOf(sp, 1.1);
    return (
        <div className={classNames(s.flyout, side === "left" ? s.left : s.right)} data-testid="siteplan-flyout">
            <div className={s.flyHead}>
                <Icon name="compass" size={13} />
                <div className={s.flyTitle}>{t("Placemaker.UI.SitePlan.Title", "Top view")}</div>
                <div className={s.flex} />
                <Btn icon="close" iconSize={12} title={t("Placemaker.UI.SitePlan.Close", "Close the large view")} onClick={() => setSitePlanBig(false)} testId="siteplan-close" />
            </div>
            <div ref={ref} className={s.flyPic} style={{ height: Math.round(600 * ratio) + "rem" }}>
                {w > 0 ? <SitePlanPicture sp={sp} w={w} h={Math.round(w * ratio)} hover={hover} hidden={hidden} hiddenKey={hiddenKeyOf(state)} /> : null}
            </div>
            <Rows sp={sp} state={state} hover={hover} setHover={setHover} columns={3} />
        </div>
    );
};
