// Stage 3 — Review, hyper-compact (s42; playtest: "way too much text bloat, alerts roll off with dot-dot-dot,
// breakdown unreadable"). Top to bottom:
//   1. Money card: Cost · Upkeep (+ area), one stacked bar of the cost by friendly bucket with a one-line legend; the
//      per-step breakdown sits behind the bar (tooltip, click to unfold).
//   2. Stat chips: icon + bold number + one word.
//   3. Alerts as short chips (2–3 words, `<key>.Short` lang keys): amber for permanent changes and warnings, red for
//      errors, blue for info; the full sentence in the tooltip; click shows it on the map / edits the step; "+N more".
//   4. Park stats: one row of five mini tiles (value + a tiny bar against the vanilla range); "Edit" unfolds the
//      sliders and explanations (ParkStatsSection).
// The "OK, permanent" tick sits beside the Build button in the footer (Panel.tsx).
import React, { useState } from "react";
import classNames from "classnames";
import { Catalog, Issue, LocText, Stat, Summary, ToolState } from "../contract";
import { act } from "../lib/bindings";
import { area as fmtArea, formatValue, Money } from "../lib/format";
import { entityIcon, layerColor, statIcon } from "../lib/icons";
import { categoryColor } from "../lib/palette";
import { humanize, TFunc, useT } from "../lib/l10n";
import { layerOf } from "../lib/schema";
import { Icon } from "../components/common";
import { Tip } from "../lib/tips";
import { useOpen } from "../lib/collapse";
import { layerName, targetLabel } from "./DesignStage";
import { recipeGroup } from "./PresetBrowser";
import { ParkStatsSection } from "./ParkStatsSection";
import s from "./review.module.scss";
import { SectionHead } from "./SectionHead";

export const StatChip = ({ k, value, unit }: { k: string; value: number; unit: string }) => {
    const { t } = useT();
    const label = t(k + ".Short", t(k, humanize(k)).toLowerCase());
    return (
        <Tip title={t(k, humanize(k))}>
            <div className={s.stat} data-testid={`stat-${k}`}>
                <Icon name={statIcon(k)} size={12} tint="dim" />
                <div className={s.statValue}>{unit === "m2" || unit === "area" ? fmtArea(value) : formatValue(value, unit === "count" ? "count" : unit)}</div>
                <div className={s.statLabel}>{label.toLowerCase()}</div>
            </div>
        </Tip>
    );
};

/**
 * (s45) One chip per label: stats of different layers that read the same ("15 trees" + "62 trees") merge. Counts and
 * lengths add up; areas take the largest (a parking lot inside the mall is not extra land).
 */
export function mergeStats(t: TFunc, stats: Stat[]): Stat[] {
    const out: Stat[] = [];
    const seen = new Map<string, Stat>();
    for (const st of stats) {
        const area = st.unit === "m2" || st.unit === "area";
        const label = t(st.key + ".Short", t(st.key, humanize(st.key))).toLowerCase();
        const k = label + "|" + (area ? "area" : st.unit);
        const hit = seen.get(k);
        if (!hit) {
            const copy = { ...st };
            seen.set(k, copy);
            out.push(copy);
        } else if (area) {
            if (st.value > hit.value) {
                hit.value = st.value;
                hit.unit = st.unit;
            }
        } else hit.value += st.value;
    }
    return out;
}

// ───────────── cost buckets ─────────────

type Bucket = { id: string; label: string; color: string; cost: number };

/** (s45) Friendly cost categories from the DLL (summary.costBuckets), with their legend names and colours. */
const BUCKET_TEXT: Record<string, { label: [string, string]; color: string }> = {
    planting: { label: ["Placemaker.UI.Bucket.Planting", "Planting"], color: "#6cc56a" },
    paths: { label: ["Placemaker.UI.Bucket.Paths", "Paths"], color: "#f5c542" },
    roads: { label: ["Placemaker.UI.Bucket.Roads", "Roads"], color: "#8fe3ff" },
    parking: { label: ["Placemaker.UI.Bucket.Parking", "Parking"], color: "#5f9dff" },
    shops: { label: ["Placemaker.UI.Bucket.Shops", "Shops"], color: "#ff7c9c" },
    water: { label: ["Placemaker.UI.Bucket.Water", "Water"], color: "#4fb8e8" },
    play: { label: ["Placemaker.UI.Bucket.Play", "Play"], color: "#ff8f5a" },
    ground: { label: ["Placemaker.UI.Bucket.Ground", "Ground"], color: "#c99a6b" },
    extras: { label: ["Placemaker.UI.Bucket.Extras", "Extras"], color: "#b48cff" },
    other: { label: ["Placemaker.UI.Bucket.Other", "Other"], color: "#9fb0b9" },
};

/** Layer-based fallback for an older DLL (no costBuckets): the type itself, or what kind of extra a layer is. */
const LAYER_BUCKETS: [RegExp, string][] = [
    [/fountain|pond|lake|shore|splash|water/i, "water"],
    [/tree|flower|plant|hedge|garden/i, "planting"],
    [/terrain|ground|decal|rock|spoil|heap|paint/i, "ground"],
    [/play|ball|court|pitch|track|skate|fitness|tennis|cricket|baseball|volley|kick|chess|golf/i, "play"],
    [/restroom|kiosk|shop|bar|picnic|stall|stand|tea/i, "shops"],
];

const bucketOf = (t: TFunc, id: string, cost: number): Bucket => {
    const x = BUCKET_TEXT[id] ?? BUCKET_TEXT.other;
    return { id, label: t(x.label[0], x.label[1]), color: x.color, cost };
};

function buckets(t: TFunc, catalog: Catalog, summary: Summary): Bucket[] {
    let list: Bucket[];
    if (summary.costBuckets && summary.costBuckets.length) {
        list = summary.costBuckets.filter((b) => b.cost > 0).map((b) => bucketOf(t, b.id, b.cost));
    } else {
        const out = new Map<string, Bucket>();
        for (const l of summary.layers) {
            if (!(l.cost > 0)) continue;
            const layer = layerOf(catalog, l.layer);
            let b: Bucket;
            if (layer.kind === "fill" && !layer.hidden) {
                const r = catalog.recipes.find((x) => x.id === layer.id);
                b = { id: "type:" + layer.id, label: layerName(t, layer), color: r ? categoryColor(recipeGroup(r)) : layerColor(layer.id, layer.kind), cost: 0 };
            } else {
                const hit = LAYER_BUCKETS.find(([re]) => re.test(layer.id));
                b = bucketOf(t, hit ? hit[1] : "extras", 0);
            }
            const cur = out.get(b.id) ?? b;
            cur.cost += l.cost;
            out.set(b.id, cur);
        }
        list = Array.from(out.values());
    }
    list.sort((a, b) => b.cost - a.cost);
    // Six buckets at most: the rest goes to "Other".
    if (list.length > 6) {
        const rest = list.splice(5);
        const other = list.find((x) => x.id === "other");
        const extra = rest.reduce((n, x) => n + x.cost, 0);
        if (other) other.cost += extra;
        else list.push(bucketOf(t, "other", extra));
    }
    return list;
}

const MoneyCard = ({ catalog, state, summary, pending }: { catalog: Catalog; state: ToolState; summary: Summary; pending: boolean }) => {
    const { t } = useT();
    const [open, setOpen] = useOpen("review:breakdown", false);
    const list = buckets(t, catalog, summary);
    const total = list.reduce((n, b) => n + b.cost, 0);
    const pct = (b: Bucket) => Math.max(1, Math.round((b.cost / total) * 100));
    const detail = summary.layers
        .map((l) => {
            const layer = layerOf(catalog, l.layer);
            const where = l.regionKey && l.regionKey !== "root" ? " · " + targetLabel(t, l.regionKey) : "";
            return layerName(t, layer) + where + ": ¢" + formatValue(Math.round(l.cost), "count");
        })
        .join("\n");
    return (
        <div className={classNames(s.money, { [s.shimmer]: pending })} data-testid="review-numbers">
            <div className={s.moneyRow}>
                <div className={s.moneyLabel} data-ellipsis>{t("Placemaker.UI.Review.Cost", "Cost")}</div>
                <div className={classNames(s.moneyValue, { [s.bad]: !summary.canAfford })} data-testid="review-cost">
                    <Money value={summary.cost} />
                </div>
                <div className={s.moneyLabel} data-ellipsis>{t("Placemaker.UI.Review.Upkeep", "Upkeep")}</div>
                <div className={s.moneyValue} data-testid="review-upkeep">
                    <Money value={summary.upkeep} perMonth />
                </div>
                <div className={s.flex} />
                {!summary.canAfford ? (
                    <div className={s.cantAfford} data-testid="cant-afford">
                        <Icon name="cost" size={11} tint="danger" />
                        <div className={s.cantText}>{t("Placemaker.UI.Review.CantAffordShort", "Too expensive")}</div>
                    </div>
                ) : (
                    <div className={s.area}>{fmtArea(state.area.areaM2)}</div>
                )}
            </div>
            {total > 0 && list.length ? (
                <Tip title={t("Placemaker.UI.Review.Breakdown", "Cost breakdown")} description={detail}>
                    <button
                        type="button"
                        className={s.barBtn}
                        data-testid="breakdown"
                        data-open={open ? "true" : "false"}
                        onClick={(e) => {
                            e.stopPropagation();
                            setOpen(!open);
                        }}
                    >
                        <div className={s.bar} data-testid="cost-bar">
                            {list.map((b) => (
                                <div key={b.id} className={s.barPart} style={{ width: (b.cost / total) * 100 + "%", backgroundColor: b.color }} />
                            ))}
                        </div>
                        <div className={s.legend}>
                            {list.map((b) => (
                                <div key={b.id} className={s.legendItem} data-testid={`bucket-${b.id}`}>
                                    <div className={s.legendDot} style={{ backgroundColor: b.color }} />
                                    <div className={s.legendText}>{b.label}</div>
                                    <div className={s.legendPct}>{pct(b) + "%"}</div>
                                </div>
                            ))}
                        </div>
                    </button>
                </Tip>
            ) : null}
            {open ? (
                <div className={s.breakdown} data-testid="breakdown-list">
                    {summary.layers.map((l, n) => {
                        const layer = layerOf(catalog, l.layer);
                        return (
                            <div key={n} className={s.breakRow}>
                                <Icon name={entityIcon(layer.id, layer.icon)} size={12} color={layerColor(layer.id, layer.kind)} />
                                <div className={s.breakName}>
                                    {layerName(t, layer)}
                                    {l.regionKey && l.regionKey !== "root" ? " · " + targetLabel(t, l.regionKey) : ""}
                                </div>
                                <div className={s.breakCost}>
                                    <Money value={l.cost} />
                                </div>
                            </div>
                        );
                    })}
                </div>
            ) : null}
        </div>
    );
};

// ───────────── alerts ─────────────

/** Alert chips shown before "+N more". */
const ALERTS_SHOWN = 5;

/** The short chip label of a long text: its `.Short` key, else its first clause, cut to whole words (never "…"). */
export function shortText(t: TFunc, loc: LocText | undefined): string {
    if (!loc || !loc.key) return t("Placemaker.UI.Review.Issue", "Issue");
    const short = t(loc.key + ".Short", "", loc.args);
    if (short) return short;
    const full = t(loc.key, humanize(loc.key), loc.args);
    const clause = full.split(/[.:;—(,]| - /)[0].trim();
    let words = clause.split(/\s+/);
    if (words.length > 4) words = words.slice(0, 3);
    // never end on a filler word ("153 trees were" → "153 trees")
    while (words.length > 1 && STOP.has(words[words.length - 1].toLowerCase())) words.pop();
    return words.join(" ");
}
const STOP = new Set(["a", "an", "the", "of", "to", "in", "on", "for", "with", "and", "or", "is", "are", "was", "were", "be", "been", "has", "have", "will", "can", "not", "no", "at", "by", "from", "this", "that", "it"]);

type Alert = { kind: "error" | "warn" | "info"; text: string; full: string; issue?: Issue; permanent?: boolean; loc?: LocText };

/**
 * (s45) One chip per thing: alerts with the same text key merge (number arguments add up: "12 trees cut"), then
 * chips that read the same merge too (the permanent "Reshapes ground" and the layer issue saying it). The first,
 * most severe one wins; the tooltip keeps every distinct sentence.
 */
export function mergeAlerts(t: TFunc, raw: Alert[]): Alert[] {
    const byKey = new Map<string, Alert>();
    const out: Alert[] = [];
    for (const a of raw) {
        const key = a.loc && a.loc.key ? (a.permanent ? "p:" : "i:") + a.loc.key : "";
        const hit = key ? byKey.get(key) : undefined;
        if (hit && hit.loc && a.loc) {
            const x = hit.loc.args ?? [],
                y = a.loc.args ?? [];
            if (x.length && x.length === y.length && x.every((v, n) => typeof v === "number" && typeof y[n] === "number"))
                hit.loc = { key: hit.loc.key, args: x.map((v, n) => (v as number) + (y[n] as number)) };
            if (hit.full.indexOf(a.full) < 0) hit.full += "\n" + a.full;
            continue;
        }
        const copy = { ...a };
        if (key) byKey.set(key, copy);
        out.push(copy);
    }
    const byText = new Map<string, Alert>();
    const merged: Alert[] = [];
    for (const a of out) {
        a.text = shortText(t, a.loc);
        const k = a.text.toLowerCase();
        const hit = byText.get(k);
        if (hit) {
            if (hit.full.indexOf(a.full) < 0) hit.full += "\n" + a.full;
            if (!hit.issue && a.issue) hit.issue = a.issue;
            continue;
        }
        byText.set(k, a);
        merged.push(a);
    }
    return merged;
}

const ALERT_ICON = { error: ["error", "danger"], warn: ["warning", "warn"], info: ["info", "accent"] } as const;

const Alerts = ({ summary }: { summary: Summary }) => {
    const { t, tl } = useT();
    const [all, setAll] = useState(false);
    const raw: Alert[] = [];
    const paintOnly = summary.permanentNeedsAck === false;
    for (const p of summary.permanent)
        raw.push({
            kind: paintOnly ? "info" : "warn",
            text: "",
            full: tl(p) + (paintOnly ? " " + t("Placemaker.UI.Review.PaintOnly", "Ground paint stays when the build is removed.") : ""),
            permanent: true,
            loc: p,
        });
    const rank = { error: 0, warning: 1, info: 2 } as Record<string, number>;
    const issues = summary.issues.map((i, n) => ({ i, n })).sort((a, b) => (rank[a.i.severity] ?? 3) - (rank[b.i.severity] ?? 3) || a.n - b.n);
    for (const { i } of issues)
        raw.push({ kind: i.severity === "error" || i.dropped ? "error" : i.severity === "warning" ? "warn" : "info", text: "", full: tl(i.text), issue: i, loc: i.text });
    // errors first, then permanent changes, warnings, info
    const order = (a: Alert) => (a.kind === "error" ? 0 : a.permanent ? 1 : a.kind === "warn" ? 2 : 3);
    raw.sort((a, b) => order(a) - order(b));
    const alerts = mergeAlerts(t, raw);
    if (!alerts.length) return null;
    const shown = all ? alerts : alerts.slice(0, ALERTS_SHOWN);
    return (
        <div className={s.alerts} data-testid="issues">
            {shown.map((a, n) => {
                const [icon, tint] = ALERT_ICON[a.kind];
                const i = a.issue;
                const action = i?.at ? t("Placemaker.UI.Review.ClickShow", "Click: show on map") : i?.stepPath ? t("Placemaker.UI.Review.ClickEdit", "Click: edit") : undefined;
                // (s61) The setting that made it happen: named with its value, one click to change it.
                const cause = i?.cause;
                const causeText = cause ? t(cause.label, humanize(cause.id)) + (cause.value ? " " + cause.value : "") : "";
                const tipText = cause ? a.full + "\n" + t("Placemaker.UI.Review.CausedBy", "Caused by the setting {0}.", [causeText]) : a.full;
                return (
                    <React.Fragment key={n}>
                    <Tip title={tipText} description={action}>
                        <button
                            type="button"
                            className={classNames(s.alert, s["alert_" + a.kind])}
                            data-testid={a.permanent ? "permanent" : `issue-${i?.severity}`}
                            onClick={(e) => {
                                e.stopPropagation();
                                if (i?.at) act.locate(i.at as [number, number, number]);
                                else if (i?.stepPath) {
                                    act.focusStep(i.stepPath);
                                    act.goToStage("design");
                                }
                            }}
                        >
                            <Icon name={icon} size={11} tint={tint} />
                            <div className={s.alertText}>{a.text}</div>
                            {i?.at ? <Icon name="marker" size={10} tint="dim" /> : null}
                        </button>
                    </Tip>
                    {cause && i?.stepPath ? (
                        <Tip title={t("Placemaker.UI.Review.ChangeSetting", "Change setting")} description={causeText}>
                            <button
                                type="button"
                                className={classNames(s.alert, s.alert_info)}
                                data-testid="issue-cause"
                                onClick={(e) => {
                                    e.stopPropagation();
                                    act.focusStep(i.stepPath!);
                                    act.goToStage("design");
                                }}
                            >
                                <div className={s.alertText}>{causeText}</div>
                            </button>
                        </Tip>
                    ) : null}
                    </React.Fragment>
                );
            })}
            {alerts.length > ALERTS_SHOWN ? (
                <button
                    type="button"
                    className={classNames(s.alert, s.alertMore)}
                    data-testid="issues-more"
                    onClick={(e) => {
                        e.stopPropagation();
                        setAll(!all);
                    }}
                >
                    <div className={s.alertText}>{all ? t("Placemaker.UI.Review.Fewer", "Fewer") : t("Placemaker.UI.Review.PlusN", "+{0} more", [alerts.length - ALERTS_SHOWN])}</div>
                </button>
            ) : null}
        </div>
    );
};

// ───────────── stage ─────────────

export const ReviewStage = ({ catalog, state, summary }: { catalog: Catalog; state: ToolState; summary: Summary }) => {
    const { t } = useT();
    const pending = summary.stale || state.planning;
    const clean = summary.issues.length === 0 && summary.permanent.length === 0 && !pending;
    return (
        <div className={s.stage} data-testid="stage-review">
            {!summary.ready ? (
                <div className={classNames(s.emptyNote, s.shimmer)} data-testid="review-planning">
                    {t("Placemaker.UI.Review.Planning", "Planning…")}
                </div>
            ) : (
                <>
                    {/* the same story as the Design page: what it costs, what you get, what to check */}
                    <SectionHead first icon="cost" title={t("Placemaker.UI.Review.CostHead", "Cost")} sub={t("Placemaker.UI.Review.CostSub", "to build and to run")} testId="review-head-cost" />
                    <MoneyCard catalog={catalog} state={state} summary={summary} pending={pending} />
                    <SectionHead icon="layers" title={t("Placemaker.UI.Review.StatsHead", "What you get")} sub={t("Placemaker.UI.Review.StatsSub", "what will be built")} testId="review-head-stats" />
                    <div className={classNames(s.stats, { [s.shimmer]: pending })} data-testid="review-stats">
                        {mergeStats(t, summary.stats).map((st) => (
                            <StatChip key={st.key + st.unit} k={st.key} value={st.value} unit={st.unit} />
                        ))}
                    </div>
                    <SectionHead
                        icon={clean ? "check" : "warning"}
                        title={t("Placemaker.UI.Review.ChecksHead", "Checks")}
                        count={summary.issues.length + summary.permanent.length}
                        tone={clean ? undefined : "warn"}
                        sub={clean ? t("Placemaker.UI.Review.AllGood", "No problems") : t("Placemaker.UI.Review.ChecksSub", "warnings and things to look at before building")}
                        testId="review-head-checks"
                    />
                    <Alerts summary={summary} />
                    {summary.parkStats && summary.parkStats.stats && summary.parkStats.stats.length ? <ParkStatsSection ps={summary.parkStats} pending={pending} /> : null}
                </>
            )}
        </div>
    );
};

export const BuildingStage = ({ state }: { state: ToolState }) => {
    const { t } = useT();
    const p = Math.max(0, Math.min(1, state.buildProgress ?? 0));
    return (
        <div className={s.stage} data-testid="stage-building">
            <div className={s.buildHead}>
                <Icon name="building" size={14} tint="accent" />
                <div className={s.buildTitle}>{t("Placemaker.UI.Building.Title", "Building…")}</div>
                <div className={s.flex} />
                <div className={s.buildPct}>{Math.round(p * 100)}%</div>
            </div>
            <div className={s.progress}>
                <div className={s.progressFill} style={{ width: p * 100 + "%" }} />
            </div>
            <div className={s.buildHint}>{t("Placemaker.UI.Building.Hint", "You can keep playing")}</div>
        </div>
    );
};
