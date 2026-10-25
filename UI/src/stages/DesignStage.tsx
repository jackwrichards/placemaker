// Stage 2 — what to make, then how it is built (s42, docs/proposals/s42.md), top to bottom, one story: every section
// opens with the same header line (SectionHead.tsx: icon badge · TITLE · note · rule · buttons).
//   1. Placemaker library: 1 Category · 2 Type · 3 Style (PresetBrowser.tsx TypeBrowser), always open; the lit Style chip
//      is the style being edited.
//   2. The style card (StyleCard.tsx, the user, 25 Oct: "I've selected the formal garden and I'm now editing it"; "it's
//      like a file"): its line is EDITING · that style's chip again, small · ● with Revert · Save · Save as… (Rename · Delete, or Reset); its
//      panel everything the style keeps, each part under the same light line: Parameters (Shuffle at its end; Zoning for
//      a zoning type: the type's control
//      groups as one-line heads in two columns, an open one unfolding full width under its pair, Controls.tsx
//      ControlList), then Add-ons (the built-in features as one-line rows in two columns: tile · name · fit or summary ·
//      switch, their options under the pair when open, the rest behind "N more").
//   3. Entrances (types that need them; this build's, not the style's): "add one to build" + typed entrance buttons on
//      the header line, then the placed ones.
//   "Places inside": reserved slot for round 3, hidden for now.
import React, { useRef } from "react";
import classNames from "classnames";
import { Catalog, DesignState, FeatureFit, FeatureView, LayerType, StepView, ToolState } from "../contract";
import { act, playSound } from "../lib/bindings";
import { setOpen as setSection, useOpen, useSections } from "../lib/collapse";
import { scrollToTop, useReveal } from "../lib/reveal";
import { entityIcon, layerColor } from "../lib/icons";
import { featureLook } from "../lib/palette";
import { humanize, TFunc, useT } from "../lib/l10n";
import { findStep, isOverridePath, layerOf, parentOf, regionLabelArgs } from "../lib/schema";
import { ErrorBoundary } from "../lib/errorBoundary";
import { usePanelLayout } from "../lib/layout";
import { Tip } from "../lib/tips";
import { Btn, Icon, Section, Switch } from "../components/common";
import { ControlList, controlsSummary } from "../controls/Controls";
import { isZoneMix, ZoneMix, ZoneOutgrow } from "../controls/ZoneMix";
import { ValueSinkContext } from "../controls/Controls";
import { SettingValue } from "../contract";
import { ControlEx, valueOf } from "../lib/schema";
import { useAssetName } from "../components/picker";
import { ZoneOutgrowInline, ZoneOutgrowPick } from "../components/PainterCard";
import { Tile, TypeBrowser } from "./PresetBrowser";
import { ShuffleBtn, StyleCard, SubHead } from "./StyleCard";
import { SectionHead } from "./SectionHead";
import s from "./design.module.scss";

export const layerName = (t: TFunc, l: LayerType) => t(l.name, humanize(l.name || l.id));

export function targetLabel(t: TFunc, target: string): string {
    if (!target) return t("Placemaker.UI.Target.Same", "whole area");
    const { tag, index } = regionLabelArgs(target);
    const tagName = t("Placemaker.UI.Tag." + tag, humanize(tag).toLowerCase());
    if (index !== null) return t("Placemaker.UI.Target.One", "{0} {1}", [tagName, index]);
    return t("Placemaker.UI.Target.Each", "each {0}", [tagName]);
}

export function regionName(t: TFunc, key: string): string {
    const { tag, index } = regionLabelArgs(key);
    const tagName = t("Placemaker.UI.Tag." + tag, humanize(tag).toLowerCase());
    const cap = tagName.charAt(0).toUpperCase() + tagName.slice(1);
    return index !== null ? `${cap} ${index}` : cap;
}

/** Heading of a step: "Roads", "Park in each block", "Plaza — Part 2", "Block 3 — Plaza". */
export function stepHeading(t: TFunc, catalog: Catalog, root: StepView | null, step: StepView): string {
    const name = layerName(t, layerOf(catalog, step.layer));
    const ov = isOverridePath(root, step.path);
    if (ov) return t("Placemaker.UI.Heading.Override", "{0} — {1}", [regionName(t, ov), name]);
    if (!step.target) return name;
    const { tag, index } = regionLabelArgs(step.target);
    if (index !== null) return t("Placemaker.UI.Heading.InOne", "{0} — {1}", [name, regionName(t, step.target)]);
    return t("Placemaker.UI.Heading.InEach", "{0} in each {1}", [name, t("Placemaker.UI.Tag." + tag, humanize(tag).toLowerCase())]);
}

// ───────────── marker kinds (entrances) ─────────────

const MARKER_FALLBACK: Record<string, [string, string, string]> = {
    entrance: ["Placemaker.UI.Marker.TwoWay", "Two-way", "twoWay"],
    twoway: ["Placemaker.UI.Marker.TwoWay", "Two-way", "twoWay"],
    entry: ["Placemaker.UI.Marker.Entry", "Entry only", "entry"],
    exit: ["Placemaker.UI.Marker.Exit", "Exit only", "exit"],
    alley: ["Placemaker.UI.Marker.Alley", "Alley", "alley"],
    footpath: ["Placemaker.UI.Marker.Footpath", "Footpath", "footpath"],
};

type MarkerKind = { kind: string; label: string; icon?: string; tooltip?: string; crosswalk?: boolean };

export function markerLabel(t: TFunc, k: MarkerKind): string {
    const fb = MARKER_FALLBACK[k.kind.toLowerCase()];
    if (k.label) return t(k.label, fb ? fb[1] : humanize(k.label));
    return fb ? t(fb[0], fb[1]) : humanize(k.kind);
}

/** Short button caption ("In" for "Entry only"), full name in the tooltip. */
const MARKER_SHORT: Record<string, [string, string]> = {
    entrance: ["Placemaker.UI.Marker.TwoWayShort", "2-way"],
    twoway: ["Placemaker.UI.Marker.TwoWayShort", "2-way"],
    entry: ["Placemaker.UI.Marker.EntryShort", "In"],
    exit: ["Placemaker.UI.Marker.ExitShort", "Out"],
    alley: ["Placemaker.UI.Marker.AlleyShort", "Driveway"],
    footpath: ["Placemaker.UI.Marker.FootpathShort", "Path"],
    pedestrian: ["Placemaker.UI.Marker.PedestrianShort", "Walk-in"],
    bike: ["Placemaker.UI.Marker.BikeShort", "Bike"],
    main: ["Placemaker.UI.Marker.MainShort", "Main"],
    service: ["Placemaker.UI.Marker.ServiceShort", "Service"],
    access: ["Placemaker.UI.Marker.AccessShort", "Access"],
};

function markerShort(t: TFunc, k: MarkerKind): string {
    const sh = MARKER_SHORT[k.kind.toLowerCase()];
    return sh ? t(sh[0], sh[1]) : markerLabel(t, k);
}

export function markerIcon(k: { kind: string; icon?: string }): string {
    if (k.icon) return k.icon;
    return MARKER_FALLBACK[k.kind.toLowerCase()]?.[2] ?? "marker";
}

const MARKER_TIPS: Record<string, [string, string]> = {
    entrance: ["Placemaker.UI.Marker.TwoWayTip", "Cars drive in and out here."],
    entry: ["Placemaker.UI.Marker.EntryTip", "Cars only drive in here."],
    exit: ["Placemaker.UI.Marker.ExitTip", "Cars only drive out here."],
    alley: ["Placemaker.UI.Marker.AlleyTip", "A small road junction through the edge."],
    footpath: ["Placemaker.UI.Marker.FootpathTip", "A footpath joins here."],
};

/**
 * Entrances: the section header ("Add one to build" while one is needed; at its end the "Place with crosswalk" switch for the entrances placed
 * next, when people walk in through any type), one button per entrance type (the lit one is being placed; click it again
 * to stop) and, while placing, what to click; then the placed entrances in two columns, each with its own Crosswalk switch,
 * show on the map and remove.
 */
const Entrances = ({ step, state }: { step: StepView; state: ToolState }) => {
    const { t } = useT();
    const kinds = step.markerKinds ?? [];
    const list = step.markerList ?? [];
    const need = step.markersRequired ?? 0;
    const have = list.length || step.markers.reduce((n, m) => n + m.count, 0);
    const placing = state.markerMode?.stepPath === step.path ? state.markerMode.kind : null;
    const byKind = (kind: string) => kinds.find((k) => k.kind === kind) ?? { kind, label: "" };
    const choose = (kind: string) => {
        if (placing === kind) {
            act.endMarkers();
            return;
        }
        if (!placing) act.startMarkers(step.path, kind);
        act.setMarkerKind(step.path, kind);
    };
    const missing = need > 0 && have < need;
    const numbers = new Map<string, number>();
    // Walking entrances come with or without a crosswalk across the street (Core StreetJoins).
    const walks = kinds.some((k) => !!k.crosswalk);
    const crossing = !!state.entranceCrosswalk;
    // The footer's "Add an entrance" leads here: scroll to it and flash.
    const ref = useRef<HTMLDivElement>(null);
    const flash = useReveal("entrances:" + step.path, () => {
        window.setTimeout(() => scrollToTop(ref.current), 30);
    });
    return (
        <div
            ref={ref}
            className={classNames(s.entrances, { [s.entrancesMissing]: missing })}
            data-testid={`entrances-wrap-${step.path}`}
            data-flash={flash ? "true" : "false"}
        >
            <SectionHead
                icon="entrance"
                title={t("Placemaker.UI.Entrances.Title", "Entrances")}
                tip={t("Placemaker.UI.Markers.Note", "Pick a type, then click the outline by a road.")}
                testId={`entrances-${step.path}`}
                tone={missing ? "warn" : undefined}
                sub={
                    missing ? (
                        <div className={s.required} data-testid="entrance-required">
                            <div className={s.requiredText}>
                                {need > 1 ? t("Placemaker.UI.Markers.NeedNShort", "Add {0} to build", [need]) : t("Placemaker.UI.Markers.NeedOneShort", "Add one to build")}
                            </div>
                        </div>
                    ) : undefined
                }
                right={
                    walks ? (
                        <Tip
                            title={t("Placemaker.UI.Markers.Crosswalk", "Place with crosswalk")}
                            description={t(
                                "Placemaker.UI.Markers.CrosswalkTip",
                                "On: the entrances you place next get a crosswalk across the street (the path joins the road at a new junction). Off: the path stops at the sidewalk and the game joins it, with no crosswalk and no cut in the road. Each placed entrance has its own switch below."
                            )}
                        >
                            <div className={s.headCross} data-testid="marker-crosswalk-wrap" data-on={crossing ? "true" : "false"}>
                                <Icon name="crosswalk" size={17} tint={crossing ? "light" : "dim"} />
                                <div className={s.headCrossLabel}>{t("Placemaker.UI.Markers.Crosswalk", "Place with crosswalk")}</div>
                                <Switch testId="marker-crosswalk" on={crossing} onChange={(v) => act.setMarkerCrosswalk(step.path, -1, v)} />
                            </div>
                        </Tip>
                    ) : undefined
                }
            />
            <div className={s.entBlock}>
                <div className={s.kindGrid} data-testid="marker-kinds">
                    {kinds.map((k) => {
                        const on = placing === k.kind;
                        const tip = k.tooltip ? t(k.tooltip, "") : MARKER_TIPS[k.kind] ? t(MARKER_TIPS[k.kind][0], MARKER_TIPS[k.kind][1]) : "";
                        return (
                            <Tip key={k.kind} title={markerLabel(t, k)} description={tip || undefined}>
                                <button
                                    type="button"
                                    data-testid={`marker-kind-${k.kind}`}
                                    aria-pressed={on ? "true" : "false"}
                                    className={classNames(s.kindBtn, { [s.kindBtnOn]: on })}
                                    onClick={(e) => {
                                        e.stopPropagation();
                                        choose(k.kind);
                                    }}
                                >
                                    <Icon name={markerIcon(k)} size={16} tint={on ? "dark" : "light"} />
                                    <div className={s.kindLabel}>{markerShort(t, k)}</div>
                                </button>
                            </Tip>
                        );
                    })}
                </div>
            </div>
            {list.length ? (
                <div className={s.entBlock}>
                    <div className={s.placedList} data-testid="marker-list">
                        {list.map((m) => {
                            const k = byKind(m.kind);
                            const n = (numbers.get(m.kind) ?? 0) + 1;
                            numbers.set(m.kind, n);
                            return (
                                <div key={m.index} className={s.placedRow} data-testid={`marker-${m.index}`} data-kind={m.kind}>
                                    <Icon name={markerIcon(k)} size={14} tint="warn" />
                                    <div className={s.placedName}>{markerShort(t, k) + " " + n}</div>
                                    <div className={s.placedGap} />
                                    {k.crosswalk ? (
                                        <Tip
                                            title={m.crosswalk ? t("Placemaker.UI.Markers.CrosswalkOn", "Crosswalk: on") : t("Placemaker.UI.Markers.CrosswalkOff", "Crosswalk: off")}
                                            description={t("Placemaker.UI.Markers.CrosswalkItemTip", "On: a crosswalk across the street here. Off: the path stops at the sidewalk.")}
                                        >
                                            <div className={s.placedCross}>
                                                <Icon name="crosswalk" size={15} tint={m.crosswalk ? "light" : "dim"} />
                                                <Switch testId={`marker-crosswalk-${m.index}`} on={!!m.crosswalk} onChange={(v) => act.setMarkerCrosswalk(step.path, m.index, v)} />
                                            </div>
                                        </Tip>
                                    ) : null}
                                    <Btn icon="eye" iconSize={13} className={s.markerBtn} title={t("Placemaker.UI.Markers.Show", "Show on map")} onClick={() => act.locate(m.position)} testId={`marker-show-${m.index}`} />
                                    <Btn icon="close" iconSize={13} className={s.markerBtn} title={t("Placemaker.UI.Markers.Remove", "Remove")} onClick={() => act.removeMarker(step.path, m.index)} testId={`marker-remove-${m.index}`} />
                                </div>
                            );
                        })}
                    </div>
                </div>
            ) : null}
        </div>
    );
};

// ───────────── places inside (round 3 slot) ─────────────

/**
 * Reserved slot for "Places inside" (docs/DESIGN.md §9). Off until the engine supports it
 * (window.__pmPlacesInside = true shows the placeholder in the mock).
 */
const PlacesInside = () => {
    const { t } = useT();
    if (!(window as any).__pmPlacesInside) return null;
    return (
        <Section
            title={t("Placemaker.UI.Places.Title", "Places inside")}
            icon="layers"
            testId="places-inside"
            right={<Btn icon="plus" iconSize={12} variant="solid" label={t("Placemaker.UI.Places.Add", "Add")} disabled testId="places-add" />}
        >
            <div className={s.placesEmpty}>{t("Placemaker.UI.Places.Empty", "Coming soon")}</div>
        </Section>
    );
};

// ───────────── extras (features) ─────────────

function featureStep(root: StepView | null, parent: StepView, f: FeatureView): StepView | null {
    if (f.path) return findStep(root, f.path);
    return parent.children.find((c) => c.layer === f.layer) ?? null;
}

/** Short verdict of a fit ("9/16 fit", "Reduced", "No room"); "" for ok. */
function fitLabel(t: TFunc, fit: FeatureFit): string {
    if (fit.kind === "failed") return t("Placemaker.UI.Fit.NoRoom", "No room");
    if (fit.kind === "reduced") {
        const a = fit.text?.args;
        if (fit.text?.key === "Placemaker.UI.Feature.SomeMissing" && a && a.length >= 2) return t("Placemaker.UI.Fit.Part", "{0}/{1} fit", [a[0], a[1]]);
        return t("Placemaker.UI.Fit.Reduced", "Reduced");
    }
    return "";
}

/** Why, from the plan (the feature layer's own issue), or what to do about "no room". */
function fitDetail(t: TFunc, fit: FeatureFit): string {
    if (!fit.text || !fit.text.key) return "";
    if (fit.text.key === "Placemaker.UI.Feature.NoRoom") return t("Placemaker.UI.Fit.NoRoomHint", "Make the area bigger or turn other extras off.");
    return t(fit.text.key, humanize(fit.text.key), fit.text.args);
}

/** Enabled features of a step that did not (fully) fit in the latest plan. */
export function misfits(step: StepView): FeatureView[] {
    return (step.features ?? []).filter((f) => f.enabled && f.status && f.status.kind !== "ok");
}

type ExtraModel = {
    f: FeatureView;
    name: string;
    why: string;
    blocked: boolean;
    child: StepView | null;
    own: string[];
    open: boolean;
    foldable: boolean;
    summary: string;
    key: string;
};

/** One add-on: [chevron] [tile] name · fit or summary · [switch], one fixed line. */
const ExtraRow = ({ m, parent, width }: { m: ExtraModel; parent: StepView; width: number }) => {
    const { t } = useT();
    const { f, name, why, blocked, open, foldable, summary } = m;
    const look = featureLook(f.layer, f.icon);
    const fit = f.enabled ? f.status : undefined;
    const bad = !!fit && fit.kind !== "ok";
    const tip = [why, f.tooltip ? t(f.tooltip, "") : ""].filter(Boolean).join(" — ");
    const head = (
        <>
            {foldable ? <Icon name={open ? "chevronDown" : "chevronRight"} size={10} tint={open ? "accent" : "dim"} /> : <div className={s.chevSpace} />}
            <div className={classNames(s.extraTile, { [s.extraTileOff]: !f.enabled })}>
                <Tile icon={look.icon} color={look.color} size={16} />
            </div>
            <Tip title={name} description={tip || undefined}>
                <div className={s.featureName} data-ellipsis>{name}</div>
            </Tip>
            {bad ? (
                <Tip title={fitLabel(t, fit!)} description={fitDetail(t, fit!) || undefined}>
                    <div
                        className={classNames(s.fitTag, fit!.kind === "failed" ? s.fitTagFailed : s.fitTagReduced, { [s.fitStale]: fit!.stale })}
                        data-testid={`feature-fit-line-${f.layer}`}
                        data-kind={fit!.kind}
                    >
                        {fitLabel(t, fit!)}
                    </div>
                </Tip>
            ) : summary ? (
                <div className={s.featureSummary} data-ellipsis data-testid={`feature-summary-${f.layer}`}>
                    {summary}
                </div>
            ) : (
                <div className={s.flex} />
            )}
        </>
    );
    return (
        <div
            className={classNames(s.feature, { [s.featureOn]: f.enabled, [s.featureOpen]: open && foldable, [s.featureBlocked]: blocked })}
            style={{ width: width + "%" }}
            data-testid={`feature-${f.layer}`}
            data-enabled={f.enabled ? "true" : "false"}
            data-open={open && foldable ? "true" : "false"}
            data-fit={fit?.kind ?? ""}
            data-disabled={why ? "true" : undefined}
            data-reason={why || undefined}
        >
            <div className={s.featureHead}>
                {foldable ? (
                    <button
                        type="button"
                        className={s.featureFold}
                        aria-expanded={open ? "true" : "false"}
                        data-testid={`feature-fold-${f.layer}`}
                        onClick={(e) => {
                            e.stopPropagation();
                            playSound(open ? "toggle-off" : "toggle-on");
                            setSection(m.key, !open);
                        }}
                    >
                        {head}
                    </button>
                ) : (
                    <div className={s.featureFold}>{head}</div>
                )}
                {fit ? <div className={classNames(s.fitIcon, { [s.fitStale]: fit.stale })} data-testid={`feature-fit-${f.layer}`} data-kind={fit.kind} data-stale={fit.stale ? "true" : "false"} /> : null}
                <Tip title={name} description={why || undefined}>
                    <div className={blocked ? s.featureSwitchOff : s.featureSwitch}>
                        <Switch on={f.enabled} onChange={(v) => (!v || !blocked) && act.setFeature(parent.path, f.layer, v)} testId={`feature-toggle-${f.layer}`} />
                    </div>
                </Tip>
            </div>
        </div>
    );
};

/** Add-ons listed before "N more" (enabled ones are always listed). */
const EXTRAS_SHOWN = 8;

const Extras = ({ catalog, design, state, step }: { catalog: Catalog; design: DesignState; state: ToolState; step: StepView }) => {
    const { t, tl } = useT();
    const assetName = useAssetName();
    const layout = usePanelLayout();
    const isOpen = useSections();
    const [more, setMore] = useOpen("extras:" + step.layer + ":more", false);
    const list = step.features ?? [];
    if (!list.length) return null;
    const cols = layout.width >= 380 ? 2 : 1;
    const auto = t("Placemaker.UI.Picker.Auto", "Automatic");
    const models: ExtraModel[] = list.map((f) => {
        const layer = layerOf(catalog, f.layer);
        const name = f.label ? t(f.label, layerName(t, layer)) : layerName(t, layer);
        const why = f.disabled ? tl(f.disabled) : "";
        const child = f.enabled ? featureStep(design.root, step, f) : null;
        const own = f.options ?? [];
        // Collapsed by default once enabled; remembered per parent layer + feature (FEEDBACK-4b #7).
        const key = "feature:" + step.layer + ">" + f.layer;
        const open = isOpen(key, false);
        const parts: string[] = [];
        if (f.enabled && own.length) parts.push(controlsSummary(step, layerOf(catalog, step.layer), t, assetName, own));
        if (child) parts.push(controlsSummary(child, layerOf(catalog, child.layer), t, assetName));
        const filled = parts.filter((p) => p);
        const foldable = f.enabled && filled.length > 0;
        const real = filled.filter((p) => p !== auto);
        const summary = foldable && !open ? (real.length ? real : filled).join(" · ") : "";
        return { f, name, why, blocked: !!why && !f.enabled, child, own, open, foldable, summary, key };
    });
    // The first ones and every enabled one are listed; the rest (off) behind "N more".
    const listed = models.map((m, i) => i < EXTRAS_SHOWN || m.f.enabled);
    const hidden = listed.filter((x) => !x).length;
    const shown = more ? models : models.filter((_, i) => listed[i]);
    const pairs: ExtraModel[][] = [];
    for (let i = 0; i < shown.length; i += cols) pairs.push(shown.slice(i, i + cols));
    return (
        <div className={s.extras} data-testid="features">
            <SubHead icon="addon" title={t("Placemaker.UI.AddOns", "Add-ons")} right={<MisfitChip step={step} catalog={catalog} />} testId="features-head" />
            <div className={s.featureGrid} data-testid="feature-grid" data-cols={cols}>
                {pairs.map((pair) => (
                    <React.Fragment key={pair.map((m) => m.f.layer).join("|")}>
                        <div className={s.extraRow}>
                            {pair.map((m) => (
                                <ExtraRow key={m.f.layer} m={m} parent={step} width={100 / cols} />
                            ))}
                        </div>
                        {pair
                            .filter((m) => m.foldable && m.open)
                            .map((m) => (
                                <div key={m.f.layer} className={s.featureBody} data-testid={`feature-options-${m.f.layer}`}>
                                    {pair.length > 1 && pair.every((p) => p.foldable && p.open) ? <div className={s.bodyTitle}>{m.name}</div> : null}
                                    <ErrorBoundary name={"feature-" + m.f.layer}>
                                        {m.own.length ? <ControlList step={step} layer={layerOf(catalog, step.layer)} state={state} only={m.own} flat /> : null}
                                        {m.child ? <ControlList step={m.child} layer={layerOf(catalog, m.child.layer)} state={state} scope={step.layer + ">" + m.f.layer} allFolded /> : null}
                                    </ErrorBoundary>
                                </div>
                            ))}
                    </React.Fragment>
                ))}
            </div>
            {hidden > 0 ? (
                <button
                    type="button"
                    className={s.moreRow}
                    data-testid="features-more"
                    onClick={(e) => {
                        e.stopPropagation();
                        setMore(!more);
                    }}
                >
                    <Icon name={more ? "chevronUp" : "chevronDown"} size={10} tint="dim" />
                    <div className={s.moreText}>{more ? t("Placemaker.UI.Extras.Fewer", "Fewer") : t("Placemaker.UI.Extras.MoreN", "{0} more", [hidden])}</div>
                </button>
            ) : null}
        </div>
    );
};

/** Parent controls that belong to a feature: shown under its row, never in the parent's sections. */
function featureOptionIds(step: StepView): Set<string> {
    const out = new Set<string>();
    for (const f of step.features ?? []) for (const id of f.options ?? []) out.add(id);
    return out;
}

/** Paths of children that are shown as features (not as separate steps). */
function featurePaths(root: StepView | null, step: StepView): Set<string> {
    const out = new Set<string>();
    for (const f of step.features ?? []) {
        const c = f.path ? findStep(root, f.path) : step.children.find((x) => x.layer === f.layer);
        if (c) out.add(c.path);
    }
    return out;
}

/** "2 didn't fit" on the Add-ons line: enabled add-ons the latest plan had to shrink or drop. */
const MisfitChip = ({ step, catalog }: { step: StepView; catalog: Catalog }) => {
    const { t } = useT();
    const list = misfits(step);
    if (!list.length) return null;
    const failed = list.some((f) => f.status!.kind === "failed");
    const names = list
        .map((f) => {
            const l = layerOf(catalog, f.layer);
            return (f.label ? t(f.label, layerName(t, l)) : layerName(t, l)) + ": " + fitLabel(t, f.status!);
        })
        .join("\n");
    return (
        <Tip title={t("Placemaker.UI.Fit.CountTitle", "Not everything fits")} description={names}>
            <div className={classNames(s.misfit, failed ? s.misfitFailed : s.misfitReduced)} data-testid={`misfits-${step.path}`} data-count={list.length}>
                <Icon name="warning" size={11} tint={failed ? "danger" : "warn"} />
                <div className={s.misfitText}>{list.length === 1 ? t("Placemaker.UI.Fit.CountOne", "1 didn't fit") : t("Placemaker.UI.Fit.Count", "{0} didn't fit", [list.length])}</div>
            </div>
        </Tip>
    );
};

// ───────────── step options ─────────────

/**
 * A Zoning type: one section. Zone the empty land or only edit the buildings there (ZoneMix), and each zone type's own
 * Outgrow: its switch, its look (the type's unless it picks one: any look, edited in Global outgrow) and its settings (the
 * look's rows, shown with that zone type's changes over them and writing into them), kept in zoneOutgrow as
 * {"<zone key>": {"on": bool, "look": id, "v": {row: value}}}.
 */
type OutgrowMap = Record<string, { on?: boolean; look?: string; v?: Record<string, SettingValue> }>;

const ZoningShape = ({ step, layer, state, scope }: { step: StepView; layer: LayerType; state: ToolState; scope: string }) => {
    const { t } = useT();
    const raw = step.values["zoneOutgrow"];
    let map: OutgrowMap = {};
    try {
        map = typeof raw === "string" && raw ? (JSON.parse(raw) as OutgrowMap) : {};
    } catch {
        map = {};
    }
    const write = (next: OutgrowMap) => act.setValue(step.path, "zoneOutgrow", JSON.stringify(next));
    const outgrow: ZoneOutgrow = {
        isOn: (key) => map[key]?.on !== false,
        setOn: (key, on) => write({ ...map, [key]: { ...(map[key] ?? {}), on } }),
        inline: (key, id, open, toggle) =>
            state.painter ? (
                <ZoneOutgrowInline
                    p={state.painter}
                    value={map[key]?.look ?? ""}
                    typeLook={state.painter.typeLook || ""}
                    on={map[key]?.on !== false}
                    setOn={(on) => write({ ...map, [key]: { ...(map[key] ?? {}), on } })}
                    open={open}
                    toggle={toggle}
                    testId={"zonemix-look-" + id.replace(/\s+/g, "-")}
                />
            ) : null,
        pick: (key, id, close) =>
            state.painter ? (
                <ZoneOutgrowPick
                    p={state.painter}
                    value={map[key]?.look ?? ""}
                    typeLook={state.painter.typeLook || ""}
                    oldChanges={Object.keys(map[key]?.v ?? {}).length}
                    onClear={() => write({ ...map, [key]: { on: map[key]?.on, look: map[key]?.look, v: {} } })}
                    onPick={(id) => {
                        write({ ...map, [key]: { on: map[key]?.on, look: id || undefined, v: {} } });
                        close();
                    }}
                    testId={"zonemix-look-" + id.replace(/\s+/g, "-") + "-pick"}
                />
            ) : null,
    };
    return (
        <div className={s.shape} data-testid="zoning">
            <SubHead icon="zone" title={t("Placemaker.UI.Zoning", "Zoning")} />
            <ErrorBoundary name="zoning">
                <ZoneMix
                    step={step}
                    layer={layer}
                    rows={(ids) => <ControlList step={step} layer={layer} state={state} only={ids} flat />}
                    found={state.painter?.areaZones ?? []}
                    outgrow={outgrow}
                />
            </ErrorBoundary>
        </div>
    );
};

const StepOptions = ({ catalog, design, state, step }: { catalog: Catalog; design: DesignState; state: ToolState; step: StepView }) => {
    const { t } = useT();
    const layer = layerOf(catalog, step.layer);
    const parent = parentOf(design.root, step.path);
    const typed = !!(step.markerKinds && step.markerKinds.length);
    const hidden = featurePaths(design.root, step);
    const subSteps = [...step.children.filter((c) => !hidden.has(c.path)), ...Object.keys(step.overrides).map((k) => step.overrides[k])];
    const scope = parent ? parent.layer + ">" + layer.id : layer.id;
    // What the style keeps: Parameters (or Zoning), Add-ons and any sub-steps; the root's go in the style card.
    const body = (
        <>
            {!parent && isZoneMix(layer) ? (
                <ZoningShape step={step} layer={layer} state={state} scope={scope} />
            ) : (
                <div className={s.shape} data-testid="shape">
                    {parent ? null : <SubHead icon="gear" title={t("Placemaker.UI.Parameters", "Parameters")} right={<ShuffleBtn step={step} />} />}
                    <ErrorBoundary name="controls">
                        <ControlList step={step} layer={layer} state={state} scope={scope} exclude={featureOptionIds(step)} />
                    </ErrorBoundary>
                </div>
            )}
            <Extras catalog={catalog} design={design} state={state} step={step} />
            {subSteps.map((c) => (
                <ErrorBoundary key={c.path} name={"step-" + c.path}>
                    <StepOptions catalog={catalog} design={design} state={state} step={c} />
                </ErrorBoundary>
            ))}
        </>
    );
    return (
        <div className={classNames(s.step, { [s.stepOff]: !step.enabled, [s.stepChild]: !!parent })} data-testid={`step-${step.path}`}>
            {parent ? (
                // Sub-steps of a mix (shelved in the picker, still supported): a one-line head.
                <div className={s.stepHead}>
                    <div className={s.stepBar} style={{ backgroundColor: layerColor(layer.id, layer.kind) }} />
                    <Icon name={entityIcon(layer.id, layer.icon)} size={14} />
                    <div className={s.stepName}>{stepHeading(t, catalog, design.root, step)}</div>
                    {!isOverridePath(design.root, step.path) ? (
                        <div className={s.stepSwitch}>
                            <Switch on={step.enabled} onChange={(v) => act.setStepEnabled(step.path, v)} testId={`enable-${step.path}`} />
                        </div>
                    ) : null}
                    <Btn icon="shuffle" iconSize={13} title={t("Placemaker.UI.Randomize", "Shuffle")} onClick={() => act.randomize(step.path)} testId={`randomize-${step.path}`} />
                    <Btn icon="reset" iconSize={13} title={t("Placemaker.UI.ResetStep", "Reset all")} onClick={() => act.resetStep(step.path)} testId={`reset-step-${step.path}`} />
                </div>
            ) : null}
            {step.enabled ? (
                <div className={s.stepBody}>
                    {!step.ready && step.nextStep && !typed ? (
                        <Tip title={t(step.nextStep.key, humanize(step.nextStep.key), step.nextStep.args)}>
                            <div className={s.nextStep} data-testid="next-step">
                                <Icon name="info" size={12} tint="accent" />
                                <div className={s.nextStepText} data-ellipsis>
                                    {t(step.nextStep.key, humanize(step.nextStep.key), step.nextStep.args)}
                                </div>
                            </div>
                        </Tip>
                    ) : null}
                    {parent ? (
                        <>
                            {typed ? <Entrances step={step} state={state} /> : null}
                            {body}
                        </>
                    ) : (
                        <>
                            <StyleCard catalog={catalog} design={design} step={step}>
                                {body}
                            </StyleCard>
                            {typed ? <Entrances step={step} state={state} /> : null}
                        </>
                    )}
                </div>
            ) : null}
        </div>
    );
};

// ───────────── stage ─────────────

export const DesignStage = ({ catalog, design, state }: { catalog: Catalog; design: DesignState; state: ToolState }) => {
    const { t } = useT();
    const root = design.root;
    return (
        <div className={s.stage} data-testid="stage-design">
            <ErrorBoundary name="recipes">
                <TypeBrowser catalog={catalog} design={design} />
            </ErrorBoundary>
            <PlacesInside />
            {!root ? (
                <div className={s.emptyNote}>{t("Placemaker.UI.PickRecipe", "Nothing picked yet: pick a category to start")}</div>
            ) : (
                <div className={s.options} data-testid="options">
                    <ErrorBoundary name={"step-" + root.path}>
                        <StepOptions catalog={catalog} design={design} state={state} step={root} />
                    </ErrorBoundary>
                </div>
            )}
        </div>
    );
};
