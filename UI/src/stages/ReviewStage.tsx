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
import { Catalog, Issue, LocText, SitePlanView, Stat, Summary, ToolState } from "../contract";
import { act, useSitePlan } from "../lib/bindings";
import { SitePlanCard } from "../components/SitePlanCard";
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

/**
 * (sketch study) What the top view's legend already says: a stat whose label is one of its rows ("552 spaces" beside
 * "Parking 552") is not repeated as a chip, and the area moves to the section's head (the user: "we just shouldn't repeat
 * ourselves"). What stays are the details the legend has no row for (EV spaces, lanes, play equipment…).
 */
const COVERED: Record<string, string> = {
    spaces: "parking", trees: "trees", bushes: "planting", flowerbeds: "planting", paths: "paths", walkways: "paths", footpaths: "paths",
    benches: "objects", lamps: "objects", bins: "objects", "road length": "roads",
};

export function extraStats(t: TFunc, stats: Stat[], sp: SitePlanView | null): Stat[] {
    const merged = mergeStats(t, stats).filter((st) => st.value !== 0);
    if (!sp) return merged;
    const rows = new Set(sp.legend.map((r) => r.id));
    return merged.filter((st) => {
        if (st.unit === "m2" || st.unit === "area") return false;
        const label = t(st.key + ".Short", t(st.key, humanize(st.key))).toLowerCase();
        const row = COVERED[label];
        return !(row && rows.has(row));
    });
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
                ) : null}
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

/** later: something the preview cannot show before the build (laterAlerts). */
type Alert = { kind: "error" | "warn" | "info"; text: string; full: string; issue?: Issue; permanent?: boolean; loc?: LocText; later?: boolean };

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

/** Every alert of the review, merged (errors first, then permanent changes, warnings, info). */
function buildAlerts(t: TFunc, tl: (loc: LocText | undefined | null, fallback?: string) => string, summary: Summary): Alert[] {
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
    return mergeAlerts(t, raw);
}

/**
 * What the preview cannot show before the build, read from the plan (the user: "show everything that won't be visible"):
 * the ground's height changes, ground paint, water filling the basins, buildings growing on zoned plots.
 */
function laterAlerts(t: TFunc, sp: SitePlanView | null): Alert[] {
    if (!sp) return [];
    const out: Alert[] = [];
    const add = (text: string, full: string) => out.push({ kind: "info", text, full, later: true });
    if (sp.heat && sp.heat.length)
        add(t("Placemaker.UI.Review.Later.Terrain", "Ground shaping shows once built"), t("Placemaker.UI.Review.Later.Terrain.Tip", "The ground is raised and lowered when you build; the preview cannot show it. Turn on Terrain in the top view to see where."));
    if (sp.fills.some((x) => x.m.startsWith("paint:")))
        add(t("Placemaker.UI.Review.Later.Paint", "Ground paint shows once built"), t("Placemaker.UI.Review.Later.Paint.Tip", "The ground is painted when you build; the preview cannot show it."));
    if (sp.fills.some((x) => x.m === "water"))
        add(t("Placemaker.UI.Review.Later.Water", "Water fills in once built"), t("Placemaker.UI.Review.Later.Water.Tip", "Ponds and pools fill with water after the build."));
    if (sp.plots && sp.plots.length)
        add(t("Placemaker.UI.Review.Later.Plots", "Buildings grow later"), t("Placemaker.UI.Review.Later.Plots.Tip", "Shops and homes grow on the zoned plots after the build, as the city needs them."));
    return out;
}

const Alerts = ({ alerts, testId }: { alerts: Alert[]; testId: string }) => {
    const { t } = useT();
    const [all, setAll] = useState(false);
    if (!alerts.length) return null;
    const shown = all ? alerts : alerts.slice(0, ALERTS_SHOWN);
    return (
        <div className={s.alerts} data-testid={testId}>
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
                            data-testid={a.later ? "later" : a.permanent ? "permanent" : `issue-${i?.severity}`}
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
                    data-testid={testId + "-more"}
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
    const { tl } = useT();
    const pending = summary.stale || state.planning;
    const sitePlan = useSitePlan();
    const alerts = buildAlerts(t, tl, summary);
    const warnings = alerts.filter((a) => a.kind !== "info");
    const checks = [...alerts.filter((a) => a.kind === "info"), ...laterAlerts(t, sitePlan && sitePlan.style !== "classic" ? sitePlan : null)];
    const parkStats = summary.parkStats && summary.parkStats.stats && summary.parkStats.stats.length ? summary.parkStats : null;
    return (
        <div className={s.stage} data-testid="stage-review">
            {!summary.ready ? (
                <div className={classNames(s.emptyNote, s.shimmer)} data-testid="review-planning">
                    {t("Placemaker.UI.Review.Planning", "Planning…")}
                </div>
            ) : (
                <>
                    {/* the same story as the Design page: what it costs, what you get, what to check */}
                    {/* cost and the park's stats together (the user: they belong together) */}
                    <SectionHead first icon="cost" title={parkStats ? t("Placemaker.UI.Review.CostStatsHead", "Cost & park stats") : t("Placemaker.UI.Review.CostHead", "Cost")} testId="review-head-cost" />
                    <MoneyCard catalog={catalog} state={state} summary={summary} pending={pending} />
                    {parkStats ? <ParkStatsSection ps={parkStats} pending={pending} /> : null}
                    {/* one section: the top view, its legend, then only the details the legend has no row for; the area in the head */}
                    <SectionHead icon="layers" title={t("Placemaker.UI.Review.StatsHead", "What you get")} sub={fmtArea(state.area.areaM2)} testId="review-head-stats" />
                    {sitePlan && sitePlan.style !== "classic" ? <SitePlanCard sp={sitePlan} state={state} layout="row" /> : null}
                    {(() => {
                        const extras = extraStats(t, summary.stats, sitePlan && sitePlan.style !== "classic" ? sitePlan : null);
                        return extras.length ? (
                            <div className={classNames(s.stats, { [s.shimmer]: pending })} data-testid="review-stats">
                                {extras.map((st) => (
                                    <StatChip key={st.key + st.unit} k={st.key} value={st.value} unit={st.unit} />
                                ))}
                            </div>
                        ) : null;
                    })()}
                    {/* warnings (problems, permanent changes) apart from the checks (notes, what shows only once built) */}
                    {warnings.length ? (
                        <>
                            <SectionHead icon="warning" title={t("Placemaker.UI.Review.WarningsHead", "Warnings")} count={warnings.length} tone="warn" testId="review-head-warnings" />
                            <Alerts alerts={warnings} testId="issues" />
                        </>
                    ) : null}
                    <SectionHead
                        icon="check"
                        title={t("Placemaker.UI.Review.ChecksHead", "Checks")}
                        count={checks.length || undefined}
                        sub={!warnings.length && !pending ? t("Placemaker.UI.Review.AllGood", "No problems") : undefined}
                        testId="review-head-checks"
                    />
                    <Alerts alerts={checks} testId="checks" />
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
