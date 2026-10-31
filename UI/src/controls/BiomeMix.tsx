// Biome mix (Core/Layers/Common/Biomes, docs/runbooks/biomes.md): the natural areas of a place as ONE card.
// P2-8 (the user, 2026-10-31: "the zone tool is a much better one: it sticks out, but it doesn't fit, and it's more easily
// draggable ... maybe each biome can be customized itself ... it needs to be really good UX"):
//   * The mix as one bar, like the zone tool's (ZoneMix.tsx): each area's share in its colour with its name inside, or above
//     or below on a line when it does not fit; the white lines between neighbours are dragged (each keeps 1 %).
//   * A click on a part (or its label) opens that biome's own box: its share, "Only this" (100 % in one click), take it out,
//     and its few settings (group "biome:<id>", BiomeKnobs.cs; the place's own lawn shows the place's planting).
//   * "Add": the biomes not in the mix as chips; one click mixes it in at 10 % (taken from the biggest) and opens its box.
//   * Under "Whole place": Patch size (the rest of the place's settings are the sections above and below).
// It writes the same settings the old sliders did (biomeLawn, biomeForest …, biomeScale), so styles, undo, locks and Shuffle
// keep working; the C# side normalises the shares anyway.
import React, { useLayoutEffect, useRef, useState } from "react";
import classNames from "classnames";
import { LayerType, StepView, ToolState } from "../contract";
import { act, playSound } from "../lib/bindings";
import { humanize, TFunc, useT } from "../lib/l10n";
import { ControlEx, valueOf } from "../lib/schema";
import { Tip } from "../lib/tips";
import { Icon } from "../components/common";
import { ControlList } from "./Controls";
import { Slider } from "./Slider";
import z from "./zoneMix.module.scss";
import b from "./biomeMix.module.scss";

/** The group of the mix's shares (C# BiomeMix.Group), its patch size (BiomeMix.ScaleId), a biome's own settings (BiomeKnobs). */
export const BIOME_GROUP = "biomes";
export const BIOME_SCALE = "biomeScale";
export const KNOB_GROUP = "biome:";

/** Each biome's colour on the bar (C# BiomeDef.Color); a new biome without one gets a quiet grey. */
const BIOME_COLOR: Record<string, string> = {
    biomeLawn: "#8cc152",
    biomeGrass: "#5fae6e",
    biomeField: "#c3dc6c",
    biomeMeadow: "#c9b45a",
    biomeFlowers: "#c86fb4",
    biomeMulch: "#7b5236",
    biomeGlade: "#5f9a4b",
    biomeForest: "#2f6b3a",
    biomeRocky: "#9a9187",
    biomeHeath: "#c7a46b",
    biomeDunes: "#e3cf9a",
    biomeShingle: "#a39d93",
    biomeWetland: "#4f8f86",
    biomeWater: "#3f7fbf",
    biomeFields: "#c9a94e",
    biomeOrchard: "#b8453a",
    biomePasture: "#6f9440",
};
/** Light parts take dark text. */
const LIGHT = new Set(["biomeLawn", "biomeGrass", "biomeField", "biomeMeadow", "biomeHeath", "biomeDunes", "biomeFields", "biomeShingle"]);

const colour = (id: string) => BIOME_COLOR[id] ?? "#8a8f87";

/** A layer with a biome mix. */
export const hasBiomeMix = (layer: LayerType) => layer.controls.some((c) => c.group === BIOME_GROUP);

/** Ids of the mix's controls and its biomes' own settings (never in Parameters). */
export const biomeIds = (layer: LayerType) =>
    new Set(layer.controls.filter((c) => c.group === BIOME_GROUP || c.group.startsWith(KNOB_GROUP)).map((c) => c.id));

/** The share control of the biome a knob group names ("biome:meadow" → biomeMeadow). */
const shareIdOf = (group: string) => {
    const id = group.slice(KNOB_GROUP.length);
    return id ? "biome" + id[0].toUpperCase() + id.slice(1) : "";
};

/** Shares that sum to 100 after one of them is set to v: the others give or take in proportion (whole numbers). */
export function balance(ids: string[], cur: Record<string, number>, id: string, v: number): Record<string, number> {
    const out: Record<string, number> = { ...cur, [id]: v };
    const others = ids.filter((x) => x !== id);
    const rest = Math.max(0, 100 - v);
    const sum = others.reduce((n, x) => n + Math.max(0, cur[x] || 0), 0);
    if (sum <= 0) {
        // nothing else in the mix: the rest goes to the first other area (the place's own ground first)
        for (const x of others) out[x] = 0;
        if (others.length && rest > 0) out[others[0]] = rest;
    } else {
        let given = 0;
        for (const x of others) {
            out[x] = Math.floor((Math.max(0, cur[x] || 0) / sum) * rest);
            given += out[x];
        }
        // the rounding left over goes to the biggest
        const big = others.reduce((a, x) => (a === null || out[x] > out[a] ? x : a), null as string | null);
        if (big !== null) out[big] += rest - given;
    }
    return out;
}

/** Whole percentages that sum to exactly 100. */
function roundAll(ids: string[], shares: Record<string, number>) {
    const out: Record<string, number> = {};
    let n = 0;
    for (const x of ids) {
        out[x] = Math.floor(shares[x]);
        n += out[x];
    }
    const order = ids.slice().sort((a, c) => (shares[c] - Math.floor(shares[c])) - (shares[a] - Math.floor(shares[a])));
    for (let i = 0; n < 100 && i < order.length; i++, n++) out[order[i]]++;
    return out;
}

export const BiomeMix = ({ step, layer, state }: { step: StepView; layer: LayerType; state: ToolState }) => {
    const { t } = useT();
    const all = layer.controls.filter((c) => c.group === BIOME_GROUP && c.id !== BIOME_SCALE) as ControlEx[];
    const scale = layer.controls.find((c) => c.id === BIOME_SCALE) as ControlEx | undefined;
    const ids = all.map((c) => c.id);
    const raw: Record<string, number> = {};
    for (const c of all) raw[c.id] = Math.max(0, Number(valueOf(step, c)) || 0);
    // shown as percentages of the mix (a design may hold weights that do not sum to 100)
    const total = ids.reduce((n, x) => n + raw[x], 0);
    const shares: Record<string, number> = {};
    for (const x of ids) shares[x] = total > 0 ? (raw[x] / total) * 100 : x === ids[0] ? 100 : 0;
    const name = (c: ControlEx) => t(c.label, humanize(c.id.replace(/^biome/, "")));
    const tip = (c: ControlEx) => (c.tooltip ? t(c.tooltip, "") : "");
    const write = (out: Record<string, number>) => {
        for (const x of ids) {
            const v = Math.max(0, Math.round(out[x] ?? 0));
            if (Math.round(raw[x]) !== v || total !== 100) act.setValue(step.path, x, v);
        }
    };
    const inMix = all.filter((c) => shares[c.id] > 0.5);
    const outMix = all.filter((c) => shares[c.id] <= 0.5);
    // each biome's own settings, by its share control
    const knobs: Record<string, string[]> = {};
    for (const c of layer.controls)
        if (c.group.startsWith(KNOB_GROUP)) (knobs[shareIdOf(c.group)] ??= []).push(c.id);
    // the open box: the clicked biome, else the biggest that has settings of its own
    const [picked, setPicked] = useState<string | null>(null);
    const [adding, setAdding] = useState(false);
    const fallback = inMix.slice().sort((a, c) => shares[c.id] - shares[a.id]).find((c) => knobs[c.id]?.length)?.id ?? inMix[0]?.id ?? null;
    const open = picked && shares[picked] > 0.5 ? picked : fallback;
    const openCtl = all.find((c) => c.id === open);
    const set = (id: string, v: number) => write(balance(ids, roundAll(ids, shares), id, Math.max(0, Math.min(100, Math.round(v)))));
    const add = (id: string) => {
        playSound("select-item");
        // 10 % taken from the biggest part, so the others keep their shares
        const cur = roundAll(ids, shares);
        const big = inMix.reduce((a, c) => (a === null || cur[c.id] > cur[a] ? c.id : a), null as string | null);
        const take = big ? Math.min(10, Math.max(1, cur[big] - 1)) : 100;
        const out = { ...cur, [id]: take };
        if (big) out[big] = cur[big] - take;
        write(out);
        setPicked(id);
        setAdding(false);
    };
    const remove = (id: string) => {
        if (inMix.length <= 1) return;
        playSound("select-item");
        set(id, 0);
        setPicked(null);
    };
    const only = (id: string) => {
        playSound("select-item");
        const out: Record<string, number> = {};
        for (const x of ids) out[x] = x === id ? 100 : 0;
        write(out);
    };
    return (
        <div className={b.mix} data-testid="biomemix">
            <Bar
                parts={inMix.map((c) => ({ id: c.id, name: name(c), share: shares[c.id], colour: colour(c.id) }))}
                open={open}
                onPick={(id) => {
                    playSound("select-item");
                    setPicked(id);
                }}
                onCommit={(out) => write({ ...roundAll(ids, shares), ...out })}
                t={t}
            />
            <div className={b.addRow}>
                {outMix.length ? (
                    <div className={classNames(b.addBtn, { [b.addBtnOn]: adding })} onClick={() => setAdding(!adding)} data-testid="biomemix-add-toggle">
                        <Icon name="plus" size={10} tint="dim" />
                        <div className={b.addText}>{t("Placemaker.UI.BiomeMix.AddButton", "Add")}</div>
                    </div>
                ) : null}
                {adding
                    ? outMix.map((c) => (
                          <Tip key={c.id} title={name(c)} description={tip(c)}>
                              <div className={b.chip} onClick={() => add(c.id)} data-testid={"biomemix-add-" + c.id}>
                                  <div className={b.swatch} style={{ backgroundColor: colour(c.id) }} />
                                  <div className={b.chipName}>{name(c)}</div>
                              </div>
                          </Tip>
                      ))
                    : null}
            </div>
            {openCtl ? (
                <div className={b.box} style={{ borderTopColor: colour(openCtl.id) }} data-testid={"biomemix-box-" + openCtl.id}>
                    <div className={b.boxHead}>
                        <div className={b.swatchBig} style={{ backgroundColor: colour(openCtl.id) }} />
                        <Tip title={name(openCtl)} description={tip(openCtl)}>
                            <div className={b.boxName}>{name(openCtl)}</div>
                        </Tip>
                        <div className={b.boxPct} data-testid="biomemix-box-share">{Math.round(shares[openCtl.id])}%</div>
                        <div className={b.grow} />
                        {inMix.length > 1 ? (
                            <Tip title={t("Placemaker.UI.BiomeMix.OnlyTip", "Make it the whole landscape")}>
                                <div className={b.btn} onClick={() => only(openCtl.id)} data-testid="biomemix-only">
                                    {t("Placemaker.UI.BiomeMix.Only", "Only this")}
                                </div>
                            </Tip>
                        ) : null}
                        <Tip title={t("Placemaker.UI.BiomeMix.Remove", "Take out of the mix")}>
                            <div className={classNames(b.btn, b.btnIcon, { [b.btnOff]: inMix.length <= 1 })} onClick={() => remove(openCtl.id)} data-testid={"biomemix-remove-" + openCtl.id}>
                                <Icon name="close" size={10} tint="dim" />
                            </div>
                        </Tip>
                    </div>
                    {knobs[openCtl.id]?.length ? (
                        <div className={b.boxBody}>
                            <ControlList step={step} layer={layer} state={state} only={knobs[openCtl.id]} flat />
                        </div>
                    ) : null}
                </div>
            ) : null}
            {scale ? (
                <>
                    <div className={b.subHead}>
                        <div className={b.subHeadText}>{t("Placemaker.UI.BiomeMix.Whole", "Whole place")}</div>
                        <div className={b.subHeadLine} />
                    </div>
                    <div className={b.row} data-testid="biomemix-scale">
                        <Tip title={t(scale.label, "Patch size")} description={scale.tooltip ? t(scale.tooltip, "") : ""}>
                            <div className={b.label}>
                                <div className={b.name}>{t(scale.label, "Patch size")}</div>
                            </div>
                        </Tip>
                        <div className={b.slider}>
                            <div className={b.ends}>
                                <div className={b.end}>{t("Placemaker.UI.BiomeMix.Small", "many small")}</div>
                                <div className={b.end}>{t("Placemaker.UI.BiomeMix.Big", "few big")}</div>
                            </div>
                            <Slider
                                value={Number(valueOf(step, scale)) || 0}
                                min={scale.min ?? 0}
                                max={scale.max ?? 100}
                                step={scale.step ?? 1}
                                unit="%"
                                onPreview={(v) => act.previewValue(step.path, BIOME_SCALE, v)}
                                onCommit={(v) => act.setValue(step.path, BIOME_SCALE, v)}
                                onEndPreview={() => act.endPreview()}
                                testId="biomemix-scale-slider"
                            />
                        </div>
                    </div>
                </>
            ) : null}
        </div>
    );
};

type Part = { id: string; name: string; share: number; colour: string };

/**
 * The mix as one bar, the zone tool's way: each part's name and share inside it, or above or below the bar on a line when it
 * does not fit; the white lines between neighbours are dragged to trade ground between them (each keeps at least 1 %). A click
 * on a part (or its label) opens its box.
 */
const Bar = ({ parts, open, onPick, onCommit, t }: { parts: Part[]; open: string | null; onPick: (id: string) => void; onCommit: (out: Record<string, number>) => void; t: TFunc }) => {
    const barRef = useRef<HTMLDivElement>(null);
    const [drag, setDrag] = useState<number[] | null>(null);
    const [width, setWidth] = useState(0);
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
    const edges: number[] = [];
    let acc = 0;
    for (let i = 0; i < parts.length - 1; i++) {
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
        const before = start.slice(0, edge).reduce((n, v) => n + v, 0);
        const pair = start[edge] + start[edge + 1];
        const apply = (clientX: number) => {
            const at = Math.round(Math.min(1, Math.max(0, (clientX - r.left) / r.width)) * 100);
            const pos = Math.min(before + pair - 1, Math.max(before + 1, at));
            cur = start.slice();
            cur[edge] = pos - before;
            cur[edge + 1] = pair - cur[edge];
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
                if (Math.round(cur[i]) !== Math.round(start[i])) out[p.id] = Math.round(cur[i]);
            });
            if (Object.keys(out).length) onCommit(out);
        };
        window.addEventListener("mousemove", move);
        window.addEventListener("mouseup", up);
    };
    // labels: inside when they fit (measured); the others stick out above or below on a line to their part, each in the first
    // free slot (above, below, then a second row out), so two small neighbours never cover each other
    let left = 0;
    const slots: { lo: number; hi: number }[][] = [[], [], [], []];   // above, below, above 2, below 2 (in % of the bar)
    const labels = parts.map((p, i) => {
        const pct = Math.round(shares[i]) + "%";
        const w = (shares[i] / 100) * width;
        const need = textW[p.id];
        const fits = need === undefined ? w > 120 : need + 6 <= w;
        const centre = left + shares[i] / 2;
        left += shares[i];
        if (fits || width <= 0) return { id: p.id, name: p.name, pct, centre, slot: fits ? -1 : 0, at: centre };
        const wPct = (((need ?? 70) + 24) / width) * 100;
        const at = Math.min(100 - wPct, Math.max(0, centre - wPct / 2));
        let slot = 0;
        for (; slot < 4; slot++) if (!slots[slot].some((r) => at < r.hi + 1 && at + wPct > r.lo - 1)) break;
        if (slot === 4) slot = 2;
        slots[slot].push({ lo: at, hi: at + wPct });
        return { id: p.id, name: p.name, pct, centre, slot, at };
    });
    const rowsAbove = labels.some((l) => l.slot === 2) ? 2 : labels.some((l) => l.slot === 0) ? 1 : 0;
    const rowsBelow = labels.some((l) => l.slot === 3) ? 2 : labels.some((l) => l.slot === 1) ? 1 : 0;
    const ROW = 21, BAR = 30, TEXT = 18;
    const padTop = rowsAbove * ROW;
    return (
        <div className={b.barBox} style={{ paddingTop: padTop + "rem", paddingBottom: rowsBelow * ROW + "rem" }} data-testid="biomemix-bar">
            {labels.map((l) => (
                <div key={"m" + l.id} className={classNames(z.barText, z.measure)} ref={(el) => (measureRefs.current[l.id] = el)}>
                    <span className={z.barName}>{l.name}</span>
                    <span className={z.barPct}>{l.pct}</span>
                </div>
            ))}
            <div className={classNames(z.bar, b.bar)} ref={barRef}>
                {parts.map((p, i) => (
                    <div
                        key={p.id}
                        className={classNames(z.barPart, b.part, { [b.partOpen]: p.id === open })}
                        style={{ width: shares[i] + "%", backgroundColor: p.colour }}
                        onClick={() => onPick(p.id)}
                        data-testid={"biomemix-part-" + p.id}
                        data-open={p.id === open}
                    >
                        {labels[i].slot < 0 ? (
                            <div className={classNames(z.barText, { [b.textDark]: LIGHT.has(p.id) })}>
                                <span className={z.barName}>{p.name}</span>
                                <span className={z.barPct}>{labels[i].pct}</span>
                            </div>
                        ) : null}
                    </div>
                ))}
                {edges.map((at, i) => (
                    <div key={"edge" + i} className={z.barEdge} style={{ left: Math.min(100, at) + "%" }} onMouseDown={(e) => onDown(e, i)} data-testid={"biomemix-edge-" + i} title={t("Placemaker.UI.BiomeMix.DragTip", "Drag to share the ground")}>
                        <div className={z.barGrip} />
                    </div>
                ))}
            </div>
            {labels
                .filter((l) => l.slot >= 0)
                .map((l) => {
                    const up = l.slot % 2 === 0, row = l.slot >= 2 ? 1 : 0;
                    const textTop = up ? padTop - ROW * (row + 1) : padTop + BAR + 3 + ROW * row;
                    const lineTop = up ? textTop + TEXT : padTop + BAR;
                    const lineH = up ? padTop - lineTop : textTop - lineTop;
                    return (
                        <React.Fragment key={"lbl" + l.id}>
                            <div className={b.cLine} style={{ left: l.centre + "%", top: lineTop + "rem", height: Math.max(2, lineH) + "rem" }} />
                            <div className={b.cBox} style={{ left: l.at + "%", top: textTop + "rem" }}>
                                <div className={classNames(z.calloutText, b.calloutText, { [b.calloutOpen]: l.id === open })} onClick={() => onPick(l.id)} data-testid={"biomemix-label-" + l.id}>
                                    <span className={b.calloutSwatch} style={{ backgroundColor: colour(l.id) }} />
                                    <span className={z.barName}>{l.name}</span>
                                    <span className={z.barPct}>{l.pct}</span>
                                </div>
                            </div>
                        </React.Fragment>
                    );
                })}
        </div>
    );
};
