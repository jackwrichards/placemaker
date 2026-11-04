// Global outgrow's numbers (state.painter.stats, NeighbourPolishSystem.Stats), as cards: Outgrown (a ring of outgrown
// buildings by outgrow type, with the ones not outgrown, and its legend), By kind (bars), Working through (the line, its
// pace, its parts), Activity (the last two minutes, three short charts one under the other: speed, queue, frames per
// second, one hover line across them), Details.
// Gameface rules kept here: one text per element (it puts separate text pieces on lines of their own), SVG sized by its
// rem box (width and height 100%, a viewBox), flexbox and margins only. Each outgrow type keeps its own colour.
import React, { useState } from "react";
import classNames from "classnames";
import { PainterState, PainterStats } from "../contract";
import { TFunc, useT } from "../lib/l10n";
import { Collapsible } from "./common";
import s from "./outgrowStats.module.scss";

/** The categorical steps for a dark surface, in their validated order (the dataviz skill's, checked on #141c24). */
/** Not outgrown, on the types map and the ring: a dark slate (the planting styles are the light greys). */
export const NOT_OUTGROWN = "#4b5361";

export const SERIES = ["#3987e5", "#d95926", "#199e70", "#c98500", "#d55181", "#008300", "#9085e9", "#e66767"];
const NOT = "rgba(255, 255, 255, 0.14)";
const OTHER = "#8a93a3";
/** The status lens's colours (PlacemakerOverlaySystem.LensTints), for its legend. */
export const LENS = [
    { color: "#78e054", key: "polished", en: "Outgrown" },
    { color: NOT_OUTGROWN, key: "not", en: "Not outgrown" },
    { color: "#3888f2", key: "planning", en: "Fitting now" },
    { color: "#fff273", key: "added", en: "Just queued" },
    { color: "#ffc76b", key: "wait0", en: "Queued <10s" },
    { color: "#ff9438", key: "wait1", en: "Queued <30s" },
    { color: "#ed5c21", key: "wait2", en: "Queued <90s" },
    { color: "#d12121", key: "wait3", en: "Queued longer" },
];

const fmt = (n: number) => Math.round(n).toLocaleString("en-US");
const fmt1 = (n: number) => (Math.round(n * 10) / 10).toLocaleString("en-US");
const pct = (part: number, whole: number) => (whole > 0 ? Math.round((part / whole) * 100) + "%" : "–");

type Slice = { key: string; label: string; value: number; color: string };

/** A card: a title (and something on its right), then its content. */
const Card = ({ title, right, children, testId }: { title: string; right?: string; children: React.ReactNode; testId?: string }) => (
    <div className={s.card} data-testid={testId}>
        <div className={s.cardHead}>
            <div className={s.cardTitle}>{title}</div>
            {right ? <div className={s.cardRight}>{right}</div> : null}
        </div>
        {children}
    </div>
);

/** Part to whole: a ring of arcs (the game's UI ignores dashed strokes) with a small gap; the hovered slice named in the
 *  middle, else the outgrown share. */
const Ring = ({ slices, center, sub, hover, setHover }: { slices: Slice[]; center: string; sub: string; hover: string | null; setHover: (k: string | null) => void }) => {
    const total = slices.reduce((a, x) => a + x.value, 0);
    const size = 100, stroke = 14, r = (size - stroke) / 2 - 2, mid = size / 2;
    const shown = slices.filter((x) => x.value > 0);
    const gap = shown.length > 1 ? 2.5 / r : 0;
    const point = (a: number) => `${(mid + r * Math.sin(a)).toFixed(2)} ${(mid - r * Math.cos(a)).toFixed(2)}`;
    let at = 0;
    const hovered = slices.find((x) => x.key === hover);
    return (
        <div className={s.ring} onMouseLeave={() => setHover(null)}>
            <svg width="100%" height="100%" viewBox={`0 0 ${size} ${size}`}>
                {total <= 0 || shown.length === 1 ? (
                    <circle cx={mid} cy={mid} r={r} fill="none" stroke={total <= 0 ? NOT : shown[0].color} strokeWidth={stroke} onMouseEnter={() => shown.length === 1 && setHover(shown[0].key)} />
                ) : (
                    shown.map((x) => {
                        const sweep = (x.value / total) * Math.PI * 2;
                        const a0 = at + gap / 2, a1 = at + Math.max(gap / 2 + 0.001, sweep - gap / 2);
                        at += sweep;
                        return (
                            <path
                                key={x.key}
                                d={`M ${point(a0)} A ${r} ${r} 0 ${a1 - a0 > Math.PI ? 1 : 0} 1 ${point(a1)}`}
                                fill="none"
                                stroke={x.color}
                                strokeWidth={hover === x.key ? stroke + 3 : stroke}
                                onMouseEnter={() => setHover(x.key)}
                            />
                        );
                    })
                )}
            </svg>
            <div className={s.ringMid}>
                <div className={s.ringBig}>{hovered ? fmt(hovered.value) : center}</div>
                <div className={s.ringSub}>{hovered ? hovered.label : sub}</div>
            </div>
        </div>
    );
};

/** The last samples of one or two series on one axis, short: the highest value at the top left, a hover line (shared by
 *  the charts under each other) that reads every series at that moment, the time along the bottom of the last one. */
const Chart = ({ series, unit, hover, setHover, axis, t }: { series: { label: string; color: string; values: number[]; area?: boolean }[]; unit: string; hover: number | null; setHover: (i: number | null) => void; axis: boolean; t: TFunc }) => {
    const n = Math.max(0, ...series.map((x) => x.values.length));
    const W = 400, H = 60;
    const top = Math.max(1, ...series.flatMap((x) => x.values));
    const max = niceCeil(top);
    const xOf = (i: number) => (n <= 1 ? W : (i / (n - 1)) * W);
    const yOf = (v: number) => H - 2 - (v / max) * (H - 8);
    const pts = (vals: number[]) => vals.map((v, i) => `${xOf(i).toFixed(1)},${yOf(v).toFixed(1)}`).join(" ");
    const at = hover ?? n - 1;
    return (
        <div className={s.chart}>
            <div className={s.chartKeys}>
                {series.map((x) => (
                    <div key={x.label} className={s.chartKey}>
                        <div className={s.line} style={{ backgroundColor: x.color }} />
                        <div className={s.chartKeyText}>{x.label}</div>
                        <div className={s.chartKeyValue}>{`${fmt1(x.values[at] ?? 0)}${unit}`}</div>
                    </div>
                ))}
            </div>
            <div
                className={s.plot}
                onMouseMove={(e) => {
                    const box = (e.currentTarget as HTMLElement).getBoundingClientRect();
                    if (n < 2 || box.width <= 0) return;
                    setHover(Math.max(0, Math.min(n - 1, Math.round(((e.clientX - box.left) / box.width) * (n - 1)))));
                }}
                onMouseLeave={() => setHover(null)}
            >
                <svg width="100%" height="100%" viewBox={`0 0 ${W} ${H}`} preserveAspectRatio="none">
                    <line x1={0} y1={6} x2={W} y2={6} stroke="rgba(255,255,255,0.07)" strokeWidth={1} />
                    <line x1={0} y1={H / 2 + 2} x2={W} y2={H / 2 + 2} stroke="rgba(255,255,255,0.07)" strokeWidth={1} />
                    <line x1={0} y1={H - 1} x2={W} y2={H - 1} stroke="rgba(255,255,255,0.18)" strokeWidth={1} />
                    {series.map((x) =>
                        x.values.length > 1 ? (
                            <React.Fragment key={x.label}>
                                {x.area ? <polygon points={`0,${H - 1} ${pts(x.values)} ${xOf(x.values.length - 1)},${H - 1}`} fill={x.color} fillOpacity={0.16} /> : null}
                                <polyline points={pts(x.values)} fill="none" stroke={x.color} strokeWidth={2} strokeLinejoin="round" strokeLinecap="round" />
                            </React.Fragment>
                        ) : null,
                    )}
                    {hover != null ? <line x1={xOf(hover)} y1={0} x2={xOf(hover)} y2={H} stroke="rgba(255,255,255,0.5)" strokeWidth={1} /> : null}
                </svg>
                <div className={s.plotMax}>{`${fmt1(max)}${unit}`}</div>
                {n < 2 ? <div className={s.plotEmpty}>{t("Placemaker.UI.Outgrow.Gathering", "Gathering…")}</div> : null}
            </div>
            {axis ? (
                <div className={s.axis}>
                    <div className={s.axisText}>{hover != null && n > 1 ? t("Placemaker.UI.Outgrow.Ago", "{0} s ago", [n - 1 - hover]) : t("Placemaker.UI.Outgrow.TwoMin", "2 min ago")}</div>
                    <div className={s.axisText}>{t("Placemaker.UI.Outgrow.Now", "now")}</div>
                </div>
            ) : null}
        </div>
    );
};

/** A round number at or above v (1, 2, 5 × 10^k). */
function niceCeil(v: number): number {
    const p = Math.pow(10, Math.floor(Math.log10(Math.max(v, 1e-6))));
    for (const m of [1, 2, 5, 10]) if (m * p >= v) return m * p;
    return 10 * p;
}

/** A duration in plain words ("about 2 min"). */
function eta(t: TFunc, seconds: number): string {
    if (seconds < 90) return t("Placemaker.UI.Outgrow.Seconds", "about {0} s", [Math.max(1, Math.round(seconds))]);
    if (seconds < 5400) return t("Placemaker.UI.Outgrow.Minutes", "about {0} min", [Math.round(seconds / 60)]);
    return t("Placemaker.UI.Outgrow.Hours", "about {0} h", [Math.round(seconds / 360) / 10]);
}

const DETAIL_NAMES: Record<string, string> = {
    plans: "Plans made",
    planAvgMs: "Plan time, average (ms)",
    planWorstMs: "Plan time, worst (ms)",
    frameWorstMs: "Worst frame for Outgrow (ms)",
    taken: "Buildings fitted",
    newFlagged: "New buildings outgrown",
    fencesMoved: "Fences moved",
    objectsMade: "Plants and props placed",
    surfacesMade: "Surfaces laid",
    lawnsWritten: "Lawns reshaped",
    paintStamps: "Ground paint stamps",
    lookAreas: "Zoned areas with an outgrow type",
};

const KIND_NAMES = [
    { key: "homes", en: "Homes" },
    { key: "shops", en: "Shops" },
    { key: "offices", en: "Offices" },
    { key: "industry", en: "Industry" },
];

/** The whole city at a glance (under the window's buttons): outgrown, waiting, frames per second. */
export const StatusStrip = ({ polished, total, st }: { polished: number; total: number; st?: PainterStats | null }) => {
    const { t } = useT();
    const L = (id: string, en: string) => t("Placemaker.UI.Outgrow." + id, en);
    const cells = [
        { key: "outgrown", value: `${fmt(polished)} / ${fmt(total)}`, label: L("StripOutgrown", "outgrown") },
        { key: "waiting", value: st ? fmt(st.toFit + st.stamps) : "–", label: L("StripWaiting", "waiting") },
        { key: "speed", value: st ? `${fmt1(st.fittedPerSecond)}/s` : "–", label: L("StripSpeed", "fitted") },
        { key: "fps", value: st ? fmt(st.fps) : "–", label: L("StripFps", "FPS") },
    ];
    return (
        <div className={s.strip} data-testid="polish-summary">
            {cells.map((c) => (
                <div key={c.key} className={s.stripCell} data-testid={"polish-summary-" + c.key}>
                    <div className={s.stripValue}>{c.value}</div>
                    <div className={s.stripLabel}>{c.label}</div>
                </div>
            ))}
        </div>
    );
};

/** Global outgrow's Numbers tab. */
export const OutgrowStats = ({ p, st, typeName, typeColor }: { p: PainterState; st: PainterStats; typeName: (id: string) => string; typeColor: (id: string) => string }) => {
    const { t } = useT();
    const L = (id: string, en: string, args?: (string | number)[]) => t("Placemaker.UI.Outgrow." + id, en, args);
    const [hover, setHover] = useState<string | null>(null);
    const [at, setAt] = useState<number | null>(null);   // one hover line across the three charts
    const total = st.kindTotal.reduce((a, b) => a + b, 0);
    const polished = st.kindPolished.reduce((a, b) => a + b, 0);
    // the types most buildings have (each in its own colour), the rest as Other, then the buildings not outgrown
    const types = Object.entries(st.looks).sort((a, b) => b[1] - a[1]);
    const top = types.slice(0, 7);
    const other = types.slice(7).reduce((a, x) => a + x[1], 0);
    const slices: Slice[] = [
        ...top.map(([id, value]) => ({ key: id || "none", label: id ? typeName(id) : L("NoType", "No type"), value, color: id ? typeColor(id) : OTHER })),
        ...(other > 0 ? [{ key: "other", label: L("Other", "Other types"), value: other, color: OTHER }] : []),
        { key: "not", label: L("NotOutgrown", "Not outgrown"), value: Math.max(0, total - polished), color: NOT },
    ];
    const line = st.toFit + st.stamps;
    const net = st.fittedPerSecond - st.addedPerSecond;
    const pace = line === 0 ? L("PaceIdle2", "Empty") : net > 0.05 ? L("PaceDone2", "Clears in {0}", [eta(t, line / net)]) : L("PaceGrowing2", "Growing");
    const work = [
        { key: "planning", en: "Fitting now", value: st.planning, color: LENS[2].color },
        { key: "puttingBack", en: "Being removed", value: st.puttingBack, color: LENS[1].color },
        { key: "pieces", en: "Pieces to place", value: st.pieces, color: "#9aa4b2" },
    ];
    return (
        <div className={s.stats} data-testid="outgrow-stats">
            <Card title={L("OutgrownByType", "Outgrown, by outgrow type")} right={L("OfAll", "{0} of {1}", [fmt(polished), fmt(total)])} testId="outgrow-card-types">
                <div className={s.typesRow}>
                    <Ring slices={slices} center={pct(polished, total)} sub={L("RingSub", "outgrown")} hover={hover} setHover={setHover} />
                    <div className={s.legend}>
                        {slices.map((x) => (
                            <div key={x.key} className={classNames(s.legendRow, { [s.legendRowOn]: hover === x.key })} onMouseEnter={() => setHover(x.key)} onMouseLeave={() => setHover(null)}>
                                <div className={s.swatch} style={{ backgroundColor: x.color }} />
                                <div className={s.legendName}>{x.label}</div>
                                <div className={s.legendValue}>{fmt(x.value)}</div>
                                <div className={s.legendPct}>{pct(x.value, total)}</div>
                            </div>
                        ))}
                    </div>
                </div>
            </Card>
            <div className={s.pair}>
                <Card title={L("ByKind", "By kind")} testId="outgrow-card-kinds">
                    {KIND_NAMES.map((k, i) => {
                        const tot = st.kindTotal[i] ?? 0;
                        const pol = st.kindPolished[i] ?? 0;
                        if (tot === 0) return null;
                        return (
                            <div key={k.key} className={s.kindRow}>
                                <div className={s.kindName}>{t("Placemaker.UI.Polish.Kind." + k.key, k.en)}</div>
                                <div className={s.bar}>
                                    <div className={s.barFill} style={{ width: `${(pol / tot) * 100}%` }} />
                                </div>
                                <div className={s.kindValue}>{`${fmt(pol)} / ${fmt(tot)}`}</div>
                            </div>
                        );
                    })}
                    {st.kindTotal.every((n) => n === 0) ? <div className={s.muted}>{L("NoBuildings", "No buildings yet")}</div> : null}
                </Card>
                <Card title={L("Queue", "Queue")} testId="outgrow-card-work">
                    <div className={s.queueBig}>
                        <div className={s.queueValue}>{fmt(line)}</div>
                        <div className={s.queueLabel}>{L("QueueWaiting", "buildings waiting")}</div>
                    </div>
                    <div className={s.queuePace}>{pace}</div>
                    {work.map((w) => (
                        <div key={w.key} className={classNames(s.queueRow, { [s.cellZero]: w.value === 0 })} data-testid={"outgrow-work-" + w.key}>
                            <div className={s.dot} style={{ backgroundColor: w.color }} />
                            <div className={s.queueRowName}>{L("Work." + w.key, w.en)}</div>
                            <div className={s.queueRowValue}>{fmt(w.value)}</div>
                        </div>
                    ))}
                </Card>
            </div>
            <Card title={L("Activity", "Activity")} right={L("Last2", "last 2 minutes")} testId="outgrow-card-activity">
                <Chart
                    t={t}
                    unit="/s"
                    hover={at}
                    setHover={setAt}
                    axis={false}
                    series={[
                        { label: L("Fitted2", "Speed: fitted"), color: SERIES[0], values: st.histFitted },
                        { label: L("Added2", "Queued"), color: SERIES[1], values: st.histAdded },
                    ]}
                />
                <Chart t={t} unit="" hover={at} setHover={setAt} axis={false} series={[{ label: L("InLine2", "Queue: waiting"), color: SERIES[3], values: st.histLine, area: true }]} />
                <Chart t={t} unit="" hover={at} setHover={setAt} axis series={[{ label: L("Fps2", "FPS"), color: SERIES[2], values: st.histFps, area: true }]} />
                <div className={s.muted}>{L("Cost2", "Outgrow's own work: {0} ms a frame", [st.msPerFrame < 0.01 ? "< 0.01" : st.msPerFrame < 1 ? (Math.round(st.msPerFrame * 100) / 100).toString() : fmt1(st.msPerFrame)])}</div>
            </Card>
            <Collapsible id="outgrow:details" title={L("Details", "Details")} icon="info" defaultOpen={false} testId="outgrow-details">
                <div className={s.details}>
                    {Object.entries(st.details).map(([k, v]) => (
                        <div key={k} className={s.detailRow}>
                            <div className={s.detailName}>{L("Detail." + k, DETAIL_NAMES[k] ?? k)}</div>
                            <div className={s.detailValue}>{k.endsWith("Ms") ? fmt1(v) : fmt(v)}</div>
                        </div>
                    ))}
                </div>
            </Collapsible>
        </div>
    );
};

/** The status lens's legend: each colour, what it means and how many buildings in view have it. */
export const LensLegend = ({ p }: { p: PainterState }) => {
    const { t } = useT();
    const counts = p.lensCounts ?? [];
    return (
        <div className={s.lensLegend} data-testid="outgrow-lens-legend">
            {LENS.map((l, i) => (
                <div key={l.key} className={classNames(s.lensItem, { [s.lensItemNone]: !(counts[i] > 0) })}>
                    <div className={s.swatch} style={{ backgroundColor: l.color }} />
                    <div className={s.legendName}>{t("Placemaker.UI.Outgrow.Lens." + l.key, l.en)}</div>
                    <div className={s.legendValue}>{fmt(counts[i] ?? 0)}</div>
                </div>
            ))}
        </div>
    );
};
