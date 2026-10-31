// Schema-driven controls: renders any layer's Catalog.layers[].controls. A new layer needs no UI code.
//   slider · toggle · choice (segmented, icons) · asset · assetMulti · markers · arrangement
// Property-inspector density: label left, editor right, one 26rem line per control. Every
// ControlSchema group is a foldable section whose header summarises its values ("90° · 6.0 m");
// only the first essential group starts open (remembered per layer + group). visibleWhen is honoured.
// Each row shows a lock when the player set the value explicitly (Randomize keeps it) and a reset
// button only when the value differs from the default. Long explanations live in tooltips; a control may
// carry one short visible line (description, s16: "what is this road role?").
import React from "react";
import classNames from "classnames";
import { ControlOptionView, LayerType, ResolvedAsset, SettingValue, StepView, SwatchStripe, ToolState } from "../contract";
import { act, playSound } from "../lib/bindings";
import { formatValue } from "../lib/format";
import { humanize, TFunc, useT } from "../lib/l10n";
import { ControlAvailability, controlAvailability, ControlEx, defaultFor, isVisible, valueOf, valuesEqual } from "../lib/schema";
import { Tip } from "../lib/tips";
import { Btn, Icon, Segmented, Switch } from "../components/common";
import { AutoMosaic, autoPool, Thumb, useAssetList, useAssetName, usePicker } from "../components/picker";
import { Slider } from "./Slider";
import { setOpen as setSectionOpen, useSections } from "../lib/collapse";
import { pairHalves, usePanelLayout } from "../lib/layout";
import s from "./controls.module.scss";

type RowCtx = { step: StepView; layer: LayerType; state: ToolState; sink?: ValueSink | null };

/**
 * Where the rows write their values: the design (null, the default), or a sink of the caller's (a Zoning type's zone keeps its
 * own copy of the Outgrow settings in one setting: ZoneMix). With a sink there is no live preview and no lock.
 */
export type ValueSink = { set: (id: string, v: SettingValue) => void };
export const ValueSinkContext = React.createContext<ValueSink | null>(null);

const label = (t: TFunc, c: ControlEx) => t(c.label, humanize(c.label || c.id));
const optLabel = (t: TFunc, o: { value: string; label: string }) => t(o.label, humanize(o.label || o.value));

export const ControlRow = ({ c, ctx, className, avail }: { c: ControlEx; ctx: RowCtx; className?: string; avail?: ControlAvailability }) => {
    const { t } = useT();
    const sink = React.useContext(ValueSinkContext);
    ctx = { ...ctx, sink };
    const { step } = ctx;
    const value = valueOf(step, c);
    const locked = step.locked.includes(c.id);
    const def = c.default !== undefined && c.default !== null ? c.default : undefined;
    // s15: a control that does not apply now is shown greyed out with the reason (never silently hidden).
    const off = !!avail?.disabled;
    const changed = !off && def !== undefined && !valuesEqual(value, def);
    const commit = (v: SettingValue) => {
        if (!off) {
            if (sink) sink.set(c.id, v);
            else act.setValue(step.path, c.id, v);
        }
    };
    const help = c.tooltip ? t(c.tooltip, "") : c.kind === "arrangement" ? t("Placemaker.UI.Arrangement.Hint", "Repeats in this order along the edges") : "";
    const tip = off && avail?.reason ? avail.reason + (help ? " — " + help : "") : help;
    // Wide editors get their own line under the label: lists, and choices whose labels wouldn't fit beside it.
    const stacked = c.kind === "arrangement" || c.kind === "assetMulti" || (c.kind === "choice" && choiceLayout(c, t) !== "inline");

    const affordances = (
        <div className={s.affordances}>
            {locked && !off && !sink ? (
                <Btn
                    icon="lock"
                    iconSize={11}
                    className={s.mini}
                    testId={`lock-${c.id}`}
                    title={t("Placemaker.UI.Locked", "Kept when randomizing")}
                    description={t("Placemaker.UI.LockedHint", "Click to let Randomize change it again.")}
                    onClick={() => act.unlockValue(step.path, c.id)}
                />
            ) : (
                <div className={s.miniSpacer} />
            )}
            {changed ? (
                <Btn
                    icon="reset"
                    iconSize={11}
                    className={s.mini}
                    testId={`reset-${c.id}`}
                    title={t("Placemaker.UI.ResetValue", "Reset to default")}
                    onClick={() => {
                        if (sink) sink.set(c.id, def as SettingValue);
                        else {
                            act.setValue(step.path, c.id, def as SettingValue);
                            act.unlockValue(step.path, c.id);
                        }
                    }}
                />
            ) : (
                <div className={s.miniSpacer} />
            )}
        </div>
    );

    return (
        <div
            className={classNames(s.row, { [s.rowStacked]: stacked, [s.rowAsset]: c.kind === "asset", [s.rowDisabled]: off }, className)}
            data-testid={`control-${c.id}`}
            data-kind={c.kind}
            data-disabled={off ? "true" : undefined}
            data-reason={off ? avail?.reason : undefined}
        >
            <div className={s.rowHead}>
                <Tip title={label(t, c)} description={tip || undefined}>
                    <div className={classNames(s.label, { [s.labelWide]: stacked || c.kind === "toggle" })}>
                        <div className={s.labelText}>{label(t, c)}</div>
                    </div>
                </Tip>
                {stacked ? null : (
                    <div className={classNames(s.editor, { [s.editorTight]: c.kind === "toggle", [s.editorOff]: off })}>{renderEditor(c, value, commit, ctx, t)}</div>
                )}
                {affordances}
            </div>
            {c.description && t(c.description, "") ? (
                <div className={s.description} data-testid={`description-${c.id}`}>
                    {t(c.description, "")}
                </div>
            ) : null}
            {stacked ? <div className={classNames(s.stackedBody, { [s.editorOff]: off })}>{renderEditor(c, value, commit, ctx, t)}</div> : null}
        </div>
    );
};

function renderEditor(c: ControlEx, value: SettingValue, commit: (v: SettingValue) => void, ctx: RowCtx, t: TFunc) {
    switch (c.kind) {
        case "slider":
            return (
                <Slider
                    testId={`slider-${c.id}`}
                    value={typeof value === "number" ? value : Number(value) || 0}
                    min={c.min ?? 0}
                    max={c.max ?? 1}
                    step={c.step ?? 1}
                    unit={c.unit}
                    onPreview={(v) => (ctx.sink ? undefined : act.previewValue(ctx.step.path, c.id, v))}
                    onCommit={(v) => commit(v)}
                    onEndPreview={() => (ctx.sink ? undefined : act.endPreview())}
                />
            );
        case "toggle":
            return (
                <div className={s.toggleCell}>
                    <Switch testId={`toggle-${c.id}`} on={value === true} onChange={(v) => commit(v)} />
                </div>
            );
        case "choice":
            return <ChoiceEditor c={c} value={String(value)} commit={commit} />;
        case "asset":
            return <AssetEditor c={c} value={typeof value === "string" ? value : ""} commit={commit} path={ctx.step.path} resolved={ctx.step.resolvedAssets?.[c.id]} />;
        case "assetMulti":
            return <AssetMultiEditor c={c} value={Array.isArray(value) ? value : []} commit={commit} path={ctx.step.path} />;
        case "arrangement":
            return <ArrangementEditor c={c} value={Array.isArray(value) ? value : []} commit={commit} />;
        case "markers":
            return <MarkersEditor c={c} ctx={ctx} />;
        default:
            return <div className={s.unknown}>{t("Placemaker.UI.UnknownControl", "Unsupported")}</div>;
    }
}

/**
 * How a choice is laid out, so labels never get cut: up to 3 short options inline beside the label;
 * up to 3 longer ones as a full-width segmented row under it; more than 3 as wrapping chips under it.
 */
export function choiceLayout(c: ControlEx, t: TFunc): "inline" | "stacked" | "chips" | "cards" {
    const opts = c.options ?? [];
    // Options that carry a picture or a detail line (road mixes) are shown as small cards, two per row.
    if (opts.some((o) => (o.swatch && o.swatch.length > 0) || !!o.detail)) return "cards";
    if (opts.length > 3) return "chips";
    const lens = opts.map((o) => optLabel(t, o).length + (o.icon ? 3 : 0));
    const total = lens.reduce((n, x) => n + x, 0);
    return total <= 15 && lens.every((x) => x <= 7) ? "inline" : "stacked";
}

const ChoiceEditor = ({ c, value, commit }: { c: ControlEx; value: string; commit: (v: SettingValue) => void }) => {
    const { t } = useT();
    const opts = c.options ?? [];
    if (choiceLayout(c, t) === "cards") {
        return (
            <div className={s.cards} data-testid={`choice-${c.id}`}>
                {opts.map((o) => (
                    <OptionCard key={o.value} c={c} o={o} on={o.value === value} commit={commit} />
                ))}
            </div>
        );
    }
    if (choiceLayout(c, t) === "chips") {
        return (
            <div className={s.chips} data-testid={`choice-${c.id}`}>
                {opts.map((o) => {
                    const on = o.value === value;
                    const chip = (
                        <button
                            type="button"
                            key={o.value}
                            data-testid={`choice-${c.id}-${o.value}`}
                            aria-pressed={on ? "true" : "false"}
                            className={classNames(s.chip, { [s.chipOn]: on })}
                            onClick={(e) => {
                                e.stopPropagation();
                                if (!on) commit(o.value);
                            }}
                        >
                            {o.icon ? <Icon name={o.icon} size={13} tint={on ? "light" : "dim"} /> : null}
                            <div className={s.chipText}>{optLabel(t, o)}</div>
                        </button>
                    );
                    // The whole name on hover, also without a tooltip when it is long (a translation may end in "…").
                    return o.tooltip || optLabel(t, o).length > 18 ? (
                        <Tip key={o.value} title={optLabel(t, o)} description={o.tooltip ? t(o.tooltip, "") : undefined}>
                            {chip}
                        </Tip>
                    ) : (
                        chip
                    );
                })}
            </div>
        );
    }
    return (
        <Segmented
            testId={`choice-${c.id}`}
            stretch
            value={value}
            onChange={(v) => commit(v)}
            options={opts.map((o) => ({
                value: o.value,
                icon: o.icon || undefined,
                label: optLabel(t, o),
                title: optLabel(t, o),
                description: o.tooltip ? t(o.tooltip, "") : undefined,
            }))}
        />
    );
};

/** One option as a card: a generated swatch, the name and a detail line ("Streets: Two-Lane · Avenues: Four-Lane"). */
const OptionCard = ({ c, o, on, commit }: { c: ControlEx; o: ControlOptionView; on: boolean; commit: (v: SettingValue) => void }) => {
    const { t } = useT();
    const detail = o.detail ? t(o.detail, "") : "";
    const card = (
        <button
            type="button"
            data-testid={`choice-${c.id}-${o.value}`}
            aria-pressed={on ? "true" : "false"}
            className={classNames(s.card, { [s.cardOn]: on })}
            onClick={(e) => {
                e.stopPropagation();
                if (!on) commit(o.value);
            }}
        >
            {o.swatch && o.swatch.length ? (
                o.swatch.some((st) => st.role === "field") ? <PaletteSwatch stripes={o.swatch} /> : <RoadSwatch stripes={o.swatch} />
            ) : (
                <div className={s.swatchNone}>{o.icon ? <Icon name={o.icon} size={14} tint="dim" /> : null}</div>
            )}
            <div className={s.cardText}>
                <div className={s.cardLabel}>{optLabel(t, o)}</div>
                {detail ? <div className={s.cardDetail}>{detail}</div> : null}
            </div>
        </button>
    );
    return o.tooltip ? (
        <Tip key={o.value} title={optLabel(t, o)} description={t(o.tooltip, "")}>
            {card}
        </Tip>
    ) : (
        card
    );
};

/**
 * A road mix as a tiny plan: one avenue running down the middle, two streets across it and a short local
 * stub, each in its role colour and at its real width relative to the others. Green edges = trees/verges,
 * a dotted centre = bike lanes. Absolute-positioned divs only (Gameface: no CSS grid).
 */
/** Four border colour longhands (Gameface: no shorthands). */
const edgeColor = (c: string): React.CSSProperties => ({ borderTopColor: c, borderRightColor: c, borderBottomColor: c, borderLeftColor: c });

/**
 * (s18) A ground palette as a tiny square: the field framed by its border band, a round focal pad in the middle,
 * a walk from the bottom edge to the pad and dark inlay corners. Absolute-positioned divs only (Gameface).
 */
export const PaletteSwatch = ({ stripes, size = 34 }: { stripes: SwatchStripe[]; size?: number }) => {
    const c = (role: string, fallback: string) => stripes.find((x) => x.role === role)?.color || fallback;
    const band = Math.max(3, Math.round(size * 0.12));
    const pad = Math.round(size * 0.42);
    const walk = Math.max(3, Math.round(size * 0.14));
    const dot = Math.max(3, Math.round(size * 0.1));
    const abs = (style: React.CSSProperties, key: string) => <div key={key} className={s.paletteBit} style={style} />;
    return (
        <div className={s.swatch} style={{ width: size + "rem", height: size + "rem", backgroundColor: c("border", "#555555") }} data-testid="palette-swatch">
            {abs({ left: band + "rem", top: band + "rem", width: size - 2 * band + "rem", height: size - 2 * band + "rem", backgroundColor: c("field", "#999999") }, "f")}
            {abs({ left: Math.round((size - walk) / 2) + "rem", top: Math.round(size / 2) + "rem", width: walk + "rem", height: Math.round(size / 2) + "rem", backgroundColor: c("path", "#aaaaaa") }, "p")}
            {abs({ left: Math.round((size - pad) / 2) + "rem", top: Math.round((size - pad) / 2) + "rem", width: pad + "rem", height: pad + "rem", borderRadius: Math.round(pad / 2) + "rem", backgroundColor: c("focal", "#bbbbbb") }, "c")}
            {abs({ left: band + 1 + "rem", top: band + 1 + "rem", width: dot + "rem", height: dot + "rem", backgroundColor: c("inlay", "#444444") }, "i1")}
            {abs({ left: size - band - 1 - dot + "rem", top: band + 1 + "rem", width: dot + "rem", height: dot + "rem", backgroundColor: c("inlay", "#444444") }, "i2")}
        </div>
    );
};

export const RoadSwatch = ({ stripes, size = 34 }: { stripes: SwatchStripe[]; size?: number }) => {
    const by = (role: string) => stripes.find((x) => x.role === role);
    const max = Math.max(1, ...stripes.map((x) => x.width || 0));
    const px = (st: SwatchStripe | undefined) => Math.max(2, Math.round(((st?.width || 8) / max) * size * 0.26));
    const street = by("street");
    const avenue = by("avenue");
    const local = by("local");
    const bar = (st: SwatchStripe | undefined, style: React.CSSProperties, key: string) =>
        st ? (
            <div key={key} className={s.swatchBar} style={{ ...style, backgroundColor: st.color, ...edgeColor(st.green ? "#4caf50" : "rgba(0, 0, 0, 0.35)") }}>
                {st.bike ? <div className={s.swatchBike} /> : null}
            </div>
        ) : null;
    const sw = px(street);
    const aw = px(avenue);
    const lw = px(local);
    return (
        <div className={s.swatch} style={{ width: size + "rem", height: size + "rem" }} data-testid="road-swatch">
            {bar(street, { left: "0rem", width: size + "rem", top: Math.round(size * 0.25 - sw / 2) + "rem", height: sw + "rem" }, "s1")}
            {bar(street, { left: "0rem", width: size + "rem", top: Math.round(size * 0.72 - sw / 2) + "rem", height: sw + "rem" }, "s2")}
            {bar(local, { left: Math.round(size * 0.8 - lw / 2) + "rem", width: lw + "rem", top: Math.round(size * 0.25) + "rem", height: Math.round(size * 0.3) + "rem" }, "l")}
            {bar(avenue, { left: Math.round(size * 0.42 - aw / 2) + "rem", width: aw + "rem", top: "0rem", height: size + "rem" }, "a")}
        </div>
    );
};

/**
 * Map preview while hovering picker tiles: previewValue with what the value would become; endPreview
 * reverts a hover (C# treats asset previews as a look only); a click commits with setValue.
 */
function previewHandlers(path: string, id: string, toValue: (ids: string[]) => SettingValue) {
    return {
        onPreview: (ids: string[]) => act.previewValue(path, id, toValue(ids)),
        onPreviewEnd: () => act.endPreview(),
    };
}

const AssetEditor = ({
    c, value, commit, path, resolved,
}: { c: ControlEx; value: string; commit: (v: SettingValue) => void; path: string; resolved?: ResolvedAsset }) => {
    const { t } = useT();
    const picker = usePicker();
    const list = useAssetList(c.assetCategory);
    const name = useAssetName();
    const item = list?.find((a) => a.id === value);
    // s16: Automatic says what you get — the prefab the latest plan really uses for this control.
    const autoItem =
        !value && resolved?.id
            ? list?.find((a) => a.id === resolved.id) ?? { id: resolved.id, name: resolved.name || name(c.assetCategory, resolved.id), icon: resolved.icon ?? "" }
            : undefined;
    const autoName = autoItem ? autoItem.name || name(c.assetCategory, autoItem.id) : "";
    return (
        <button
            type="button"
            className={s.assetButton}
            data-testid={`asset-button-${c.id}`}
            onClick={(e) => {
                e.stopPropagation();
                picker.open({
                    category: c.assetCategory ?? "",
                    title: t(c.label, humanize(c.label || c.id)),
                    mode: "single",
                    selected: value ? [value] : [],
                    allowAutomatic: true,
                    autoItem,
                    onPick: (ids) => commit(ids[0] ?? ""),
                    ...previewHandlers(path, c.id, (ids) => ids[0] ?? ""),
                });
            }}
        >
            {value ? (
                <Thumb item={item ?? { id: value, name: name(c.assetCategory, value), icon: "" }} size={28} testId={`asset-thumb-${c.id}`} />
            ) : autoItem ? (
                <div className={s.autoThumb}>
                    <Thumb item={autoItem} size={28} testId={`asset-thumb-${c.id}`} />
                    <div className={s.autoBadge}>
                        <Icon name="auto" size={9} tint="accent" />
                    </div>
                </div>
            ) : (
                <AutoMosaic items={autoPool(list, c.assetCategory, 3)} size={28} testId={`asset-thumb-${c.id}`} />
            )}
            {/* the game's asset names run long in every language: a deliberate "…" (the picker shows them whole) */}
            <div className={s.assetName} data-testid={`asset-name-${c.id}`} data-ellipsis>
                {value
                    ? list?.find((a) => a.id === value)?.name ?? (resolved?.id === value && resolved.name ? resolved.name : name(c.assetCategory, value))
                    : autoName
                      ? t("Placemaker.UI.Picker.AutoIs", "Automatic · {0}", [autoName])
                      : t("Placemaker.UI.Picker.Auto", "Automatic")}
            </div>
            <Icon name="chevronRight" size={11} tint="dim" />
        </button>
    );
};

const AssetMultiEditor = ({ c, value, commit, path }: { c: ControlEx; value: string[]; commit: (v: SettingValue) => void; path: string }) => {
    const { t } = useT();
    const picker = usePicker();
    const name = useAssetName();
    const list = useAssetList(c.assetCategory);
    const itemOf = (id: string) => list?.find((a) => a.id === id) ?? { id, name: name(c.assetCategory, id), icon: "" };
    const openPicker = () =>
        picker.open({
            category: c.assetCategory ?? "",
            title: t(c.label, humanize(c.label || c.id)),
            mode: "multi",
            selected: value,
            allowAutomatic: true,
            onPick: (ids) => commit(ids),
            ...previewHandlers(path, c.id, (ids) => ids),
        });
    return (
        <div className={s.chips} data-testid={`assetmulti-${c.id}`}>
            {value.length === 0 ? (
                <button
                    type="button"
                    className={classNames(s.chip, s.chipAuto, s.chipThumb)}
                    data-testid={`assetmulti-auto-${c.id}`}
                    onClick={(e) => {
                        e.stopPropagation();
                        openPicker();
                    }}
                >
                    <AutoMosaic items={autoPool(list, c.assetCategory, 3)} size={20} testId={`asset-thumb-${c.id}`} />
                    <div className={s.chipText}>{t("Placemaker.UI.Picker.Auto", "Automatic")}</div>
                </button>
            ) : (
                value.map((id) => (
                    <div key={id} className={classNames(s.chip, s.chipOn, s.chipThumb)} data-testid={`chip-${id}`}>
                        <Thumb item={itemOf(id)} size={20} testId={`chip-thumb-${id}`} />
                        <div className={s.chipText}>{name(c.assetCategory, id)}</div>
                        <button
                            type="button"
                            className={s.chipX}
                            data-testid={`chip-remove-${id}`}
                            onClick={(e) => {
                                e.stopPropagation();
                                commit(value.filter((x) => x !== id));
                            }}
                        >
                            <Icon name="close" size={10} />
                        </button>
                    </div>
                ))
            )}
            <Btn icon="plus" iconSize={12} label={t("Placemaker.UI.Choose", "Choose")} className={s.chipAdd} onClick={openPicker} testId={`assetmulti-add-${c.id}`} />
        </div>
    );
};

const ArrangementEditor = ({ c, value, commit }: { c: ControlEx; value: string[]; commit: (v: SettingValue) => void }) => {
    const { t } = useT();
    const picker = usePicker();
    const name = useAssetName();
    const arrList = useAssetList(c.assetCategory);
    const move = (i: number, d: number) => {
        const j = i + d;
        if (j < 0 || j >= value.length) return;
        const next = value.slice();
        [next[i], next[j]] = [next[j], next[i]];
        commit(next);
    };
    return (
        <div className={s.chips} data-testid={`arrangement-${c.id}`}>
            {value.map((id, i) => (
                <div key={i + id} className={s.arrItem}>
                    <div className={s.arrIndex}>{i + 1}</div>
                    <Thumb item={arrList?.find((a) => a.id === id) ?? { id, name: name(c.assetCategory, id), icon: "" }} size={16} />
                    <div className={s.chipText}>{name(c.assetCategory, id)}</div>
                    <button type="button" className={s.chipX} disabled={i === 0} onClick={(e) => { e.stopPropagation(); move(i, -1); }}>
                        <Icon name="chevronLeft" size={10} />
                    </button>
                    <button type="button" className={s.chipX} onClick={(e) => { e.stopPropagation(); move(i, 1); }}>
                        <Icon name="chevronRight" size={10} />
                    </button>
                    <button
                        type="button"
                        className={s.chipX}
                        data-testid={`arr-remove-${i}`}
                        onClick={(e) => {
                            e.stopPropagation();
                            commit(value.filter((_, k) => k !== i));
                        }}
                    >
                        <Icon name="close" size={10} />
                    </button>
                </div>
            ))}
            <Btn
                icon="plus"
                iconSize={12}
                label={t("Placemaker.UI.Add", "Add")}
                className={s.chipAdd}
                testId={`arrangement-add-${c.id}`}
                onClick={() =>
                    picker.open({
                        category: c.assetCategory ?? "",
                        title: t(c.label, humanize(c.label || c.id)),
                        mode: "append",
                        selected: [],
                        allowAutomatic: false,
                        onPick: (ids) => ids[0] && commit([...value, ids[0]]),
                    })
                }
            />
        </div>
    );
};

const MarkersEditor = ({ c, ctx }: { c: ControlEx; ctx: RowCtx }) => {
    const { t } = useT();
    const kinds = c.markerKinds && c.markerKinds.length ? c.markerKinds : [c.id];
    return (
        <div className={s.markers}>
            {kinds.map((kind) => {
                const count = ctx.step.markers.find((m) => m.kind === kind)?.count ?? 0;
                const active = ctx.state.markerMode?.stepPath === ctx.step.path && ctx.state.markerMode?.kind === kind;
                return (
                    <div key={kind} className={s.markerRow}>
                        <Btn
                            icon="marker"
                            iconSize={12}
                            variant={active ? "accent" : "solid"}
                            testId={`markers-${kind}`}
                            label={
                                active
                                    ? t("Placemaker.UI.Markers.Active", "Done ({0})", [count])
                                    : count
                                    ? t("Placemaker.UI.Markers.PlaceN", "Place ({0})", [count])
                                    : t("Placemaker.UI.Markers.Place", "Place")
                            }
                            description={t("Placemaker.UI.Markers.Hint", "Click the outline to add; right-click takes back the last one.")}
                            onClick={() => (active ? act.endMarkers() : act.startMarkers(ctx.step.path, kind))}
                        />
                        {count > 0 && !active ? (
                            <Btn
                                icon="close"
                                iconSize={11}
                                testId={`markers-clear-${kind}`}
                                title={t("Placemaker.UI.Markers.Clear", "Remove all")}
                                onClick={() => act.clearMarkers(ctx.step.path, kind)}
                            />
                        ) : null}
                    </div>
                );
            })}
        </div>
    );
};

// ───────────── group summaries ─────────────

/**
 * One-line summary of a group's values for its folded header: "90° · 6.0 m · Concrete". A group led
 * by an off switch reads "Off"; automatic assets, empty lists and zero counts are left out.
 */
export function summarize(
    list: ControlEx[],
    step: StepView,
    t: TFunc,
    assetName: (category: string | undefined, id: string) => string,
): string {
    if (!list.length) return "";
    // A "master" switch leads its group (the rest only shows when it is on): off → the group is "Off".
    const first = list[0];
    const master = first.kind === "toggle" && (list.length === 1 || list.slice(1).every((c) => (c.visibleWhen ?? "").includes(first.id)));
    if (master && valueOf(step, first) !== true) return t("Placemaker.UI.Off", "Off");
    const parts: string[] = [];
    for (const c of list) {
        const v = valueOf(step, c);
        switch (c.kind) {
            case "toggle":
                if (v === true && !(master && c === first)) parts.push(label(t, c));
                break;
            case "slider": {
                const n = typeof v === "number" ? v : Number(v) || 0;
                if (c.unit === "count") {
                    if (n > 0) parts.push(label(t, c) + " " + formatValue(n, "count"));
                } else parts.push(formatValue(n, c.unit, c.step));
                break;
            }
            case "choice": {
                const o = (c.options ?? []).find((x) => x.value === String(v));
                if (o) parts.push(optLabel(t, o));
                break;
            }
            case "asset":
                if (typeof v === "string" && v) parts.push(assetName(c.assetCategory, v));
                break;
            case "assetMulti":
            case "arrangement":
                if (Array.isArray(v) && v.length) parts.push(label(t, c) + " " + v.length);
                break;
            case "markers": {
                const n = step.markers.reduce((k, m) => k + ((c.markerKinds ?? [c.id]).includes(m.kind) ? m.count : 0), 0);
                if (n) parts.push(label(t, c) + " " + n);
                break;
            }
        }
    }
    if (master && !parts.length) return t("Placemaker.UI.On", "On");
    if (!parts.length) return t("Placemaker.UI.Picker.Auto", "Automatic");
    // Keep it to what fits one header line (~30 characters): the first values win.
    let out = parts[0];
    for (const p of parts.slice(1, 3)) {
        if (out.length + 3 + p.length > 30) break;
        out += " · " + p;
    }
    return out;
}

/**
 * Controls of one step as foldable groups (Control.group), groups holding essentials first and
 * essentials first inside each group. Only the first essential group starts open (unless
 * `allFolded`), and each group's state is remembered under "<scope>:<group>".
 * Marker controls are left out when the step declares markerKinds: the Entrances section shows them.
 */
export const ControlList = ({
    step, layer, state, scope, allFolded, only, exclude, flat,
}: RowCtx & {
    scope?: string;
    allFolded?: boolean;
    /** Only these control ids (a feature's options living on the parent step). */
    only?: string[];
    /** Leave these ids out (they are shown under their feature toggle instead). */
    exclude?: Set<string>;
    /** Plain rows, no group sections. */
    flat?: boolean;
}) => {
    const { t } = useT();
    const assetName = useAssetName();
    const layout = usePanelLayout();
    const isOpen = useSections();
    const { tl } = useT();
    const ctx = { step, layer, state };
    /**
     * Wide panel: short rows (sliders, switches) pair up in two columns; lists, choices and assets keep
     * the full width. Narrow: one column.
     */
    const rows = (list: ControlEx[]) => {
        if (!layout.wide) return list.map((c) => <ControlRow key={c.id} c={c} ctx={ctx} avail={avail(c)} />);
        // A half row keeps a 92rem label: only short labels pair up (long ones keep the full width, never cut).
        const half = pairHalves(list.map((c) => (c.kind === "slider" || c.kind === "toggle") && label(t, c).length <= 13));
        return (
            <div className={s.grid} data-testid="control-grid">
                {list.map((c, i) => (
                    <ControlRow key={c.id} c={c} ctx={ctx} className={half[i] ? s.half : s.full} avail={avail(c)} />
                ))}
            </div>
        );
    };
    const typedMarkers = !!(step.markerKinds && step.markerKinds.length);
    const values = withDefaults(layer, step);
    // s15: greyed out instead of hidden (lib/schema controlAvailability); only hideWhenOff controls vanish.
    const availability = new Map<string, ControlAvailability>();
    const avail = (c: ControlEx) => {
        let a = availability.get(c.id);
        if (!a) {
            const area = step.disabled?.[c.id];
            a = controlAvailability(c, values, layer.controls as ControlEx[], t, area ? tl(area) : undefined);
            availability.set(c.id, a);
        }
        return a;
    };
    const visible = (layer.controls as ControlEx[]).filter(
        (c) =>
            avail(c).show &&
            !(typedMarkers && c.kind === "markers") &&
            (!only || only.includes(c.id)) &&
            !(exclude && exclude.has(c.id)),
    );
    if (visible.length === 0) {
        return typedMarkers || only || exclude?.size ? null : <div className={s.none}>{t("Placemaker.UI.NoSettings", "No settings: adapts to the area.")}</div>;
    }
    if (flat) {
        return (
            <div className={s.list} data-testid="control-list-flat">
                {rows(visible)}
            </div>
        );
    }
    const groups = groupBy(visible);
    const key = scope ?? layer.id;
    // s42: every section starts folded (one line each); the player opens what they need, remembered per type.
    const defaultOpen = (_i: number, _list: ControlEx[]) => false;
    // s42: sections as fixed one-line heads in two columns (chevron · name · summary). A head never moves:
    // an open section's controls unfold full width under the pair of heads it belongs to.
    const cols = layout.width >= SECTION_TWO_COL_REM ? 2 : 1;
    const items = groups.map(([g, list], i) => {
        const name = g || "basics";
        const id = key + ":" + name;
        return {
            name, list, id,
            open: isOpen(id, defaultOpen(i, list)),
            title: g ? t("Placemaker.UI.Group." + g, humanize(g)) : allFolded ? t("Placemaker.UI.Group.Options", "Options") : t("Placemaker.UI.Group.Basics", "Basics"),
        };
    });
    const pairs: (typeof items)[] = [];
    for (let i = 0; i < items.length; i += cols) pairs.push(items.slice(i, i + cols));
    return (
        <div className={classNames(s.list, s.sections, { [s.sectionsNested]: allFolded })} data-testid="control-list" data-cols={cols}>
            {pairs.map((pair) => (
                <React.Fragment key={pair.map((x) => x.name).join("|")}>
                    <div className={s.secRow}>
                        {pair.map((x) => (
                            <SectionHead key={x.name} id={x.id} name={x.name} title={x.title} open={x.open} summary={summarize(x.list, step, t, assetName)} width={100 / cols} />
                        ))}
                    </div>
                    {pair
                        .filter((x) => x.open)
                        .map((x) => (
                            <div key={x.name} className={s.secBody} data-testid={"group-" + x.name + "-body"}>
                                {pair.length > 1 && pair.every((p) => p.open) ? <div className={s.secBodyTitle}>{x.title}</div> : null}
                                {rows(x.list)}
                            </div>
                        ))}
                </React.Fragment>
            ))}
        </div>
    );
};

/** From this panel width on (rem), section heads sit in two columns. */
const SECTION_TWO_COL_REM = 380;

/** One section head: a fixed 24rem line; the summary (right-aligned, cut with an ellipsis) only while folded. */
const SectionHead = ({ id, name, title, open, summary, width }: { id: string; name: string; title: string; open: boolean; summary: string; width: number }) => (
    <div className={classNames(s.secCell, { [s.secCellOpen]: open })} style={{ width: width + "%" }} data-testid={"group-" + name} data-open={open ? "true" : "false"}>
        <Tip title={title} description={summary || undefined}>
            <button
                type="button"
                aria-expanded={open ? "true" : "false"}
                data-testid={"group-" + name + "-toggle"}
                className={s.secHead}
                onClick={(e) => {
                    e.stopPropagation();
                    playSound(open ? "toggle-off" : "toggle-on");
                    setSectionOpen(id, !open);
                }}
            >
                <Icon name={open ? "chevronDown" : "chevronRight"} size={11} tint={open ? "accent" : "dim"} />
                <div className={s.secName} data-ellipsis>{title}</div>
                {summary && !open ? (
                    <div className={s.secSummary} data-ellipsis data-testid={"group-" + name + "-summary"}>
                        {summary}
                    </div>
                ) : (
                    <div className={s.secSpacer} />
                )}
            </button>
        </Tip>
    </div>
);

/**
 * Values summary of a step's visible controls (optionally only some ids), for a folded header such as a
 * feature row: "Circle · Centre · 15%". "" when nothing is visible. Hook-free: pass useAssetName().
 */
export function controlsSummary(
    step: StepView,
    layer: LayerType,
    t: TFunc,
    assetName: (category: string | undefined, id: string) => string,
    only?: string[],
): string {
    const values = withDefaults(layer, step);
    const typedMarkers = !!(step.markerKinds && step.markerKinds.length);
    const all = groupBy(
        (layer.controls as ControlEx[]).filter(
            (c) => isVisible(c.visibleWhen, values) && !(typedMarkers && c.kind === "markers") && (!only || only.includes(c.id)),
        ),
    ).flatMap(([, l]) => l);
    // Essentials say what the feature is; "more" settings only when there is nothing else.
    const essentials = all.filter((c) => c.tier !== "more");
    const list = essentials.length ? essentials : all;
    return list.length ? summarize(list, step, t, assetName) : "";
}

function withDefaults(layer: LayerType, step: StepView): Record<string, SettingValue> {
    const v: Record<string, SettingValue> = {};
    for (const c of layer.controls as ControlEx[]) v[c.id] = defaultFor(c);
    return { ...v, ...step.values };
}

function groupBy(list: ControlEx[]): [string, ControlEx[]][] {
    const out: [string, ControlEx[]][] = [];
    for (const c of list) {
        const g = c.group || "";
        const hit = out.find((x) => x[0] === g);
        if (hit) hit[1].push(c);
        else out.push([g, [c]]);
    }
    const ess = (l: ControlEx[]) => l.some((c) => c.tier !== "more");
    for (const entry of out) entry[1] = [...entry[1].filter((c) => c.tier !== "more"), ...entry[1].filter((c) => c.tier === "more")];
    // stable: groups with essentials first, the unnamed group first among equals
    return out
        .map((e, i) => ({ e, i }))
        .sort((a, b) => Number(ess(b.e[1])) - Number(ess(a.e[1])) || Number(b.e[0] === "") - Number(a.e[0] === "") || a.i - b.i)
        .map((x) => x.e);
}

/** "850 m" / "1.2 km". */
export function formatLength(m: number): string {
    return m >= 1000 ? (m / 1000).toFixed(1) + " km" : Math.round(m) + " m";
}
