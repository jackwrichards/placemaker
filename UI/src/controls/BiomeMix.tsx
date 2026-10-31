// Biome mix (Core/Layers/Common/Biomes, docs/runbooks/biomes.md): the natural areas of a place as ONE control instead of a
// slider per biome. The user: "percentages of each ... a super rocky area or a super forest area or just a mulch or a flower
// field ... each subtype could tweak them".
//   * A bar of the mix: each area's share in its colour, the dividers dragged to trade between neighbours.
//   * A line per area in the mix: its colour, name, a slider (the others give or take in proportion, so it stays 100 %), remove.
//   * The areas not in the mix as small chips: a click mixes one in at 10 %.
//   * Patch size: many small patches .. a few big areas.
// It writes the same settings the sliders would (biomeLawn, biomeForest …, biomeScale), so styles, undo, locks and Shuffle
// keep working; the C# side normalises the shares anyway.
import React, { useLayoutEffect, useRef, useState } from "react";
import classNames from "classnames";
import { LayerType, StepView } from "../contract";
import { act, playSound } from "../lib/bindings";
import { humanize, useT } from "../lib/l10n";
import { ControlEx, valueOf } from "../lib/schema";
import { Tip } from "../lib/tips";
import { Icon } from "../components/common";
import { Slider } from "./Slider";
import z from "./zoneMix.module.scss";
import b from "./biomeMix.module.scss";

/** The group of the mix's controls (C# BiomeMix.Group) and its patch size (BiomeMix.ScaleId). */
export const BIOME_GROUP = "biomes";
export const BIOME_SCALE = "biomeScale";

/** Each biome's colour on the bar (C# BiomeDef.Color); a new biome without one gets a quiet grey. */
const BIOME_COLOR: Record<string, string> = {
    biomeLawn: "#8cc152",
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

/** A layer with a biome mix. */
export const hasBiomeMix = (layer: LayerType) => layer.controls.some((c) => c.group === BIOME_GROUP);

/** Ids of the mix's controls (never in Parameters). */
export const biomeIds = (layer: LayerType) => new Set(layer.controls.filter((c) => c.group === BIOME_GROUP).map((c) => c.id));

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

export const BiomeMix = ({ step, layer }: { step: StepView; layer: LayerType }) => {
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
    const set = (id: string, v: number) => write(balance(ids, roundAll(ids, shares), id, Math.max(0, Math.min(100, Math.round(v)))));
    const add = (id: string) => {
        playSound("select-item");
        set(id, 10);
    };
    const remove = (id: string) => {
        playSound("select-item");
        if (inMix.length <= 1) return;
        set(id, 0);
    };
    return (
        <div className={b.mix} data-testid="biomemix">
            <Bar parts={inMix.map((c) => ({ id: c.id, name: name(c), share: shares[c.id], colour: BIOME_COLOR[c.id] ?? "#8a8f87" }))} onCommit={(out) => write({ ...roundAll(ids, shares), ...out })} t={t} />
            {inMix.map((c) => (
                <div key={c.id} className={b.row} data-testid={"biomemix-row-" + c.id}>
                    <Tip title={name(c)} description={tip(c)}>
                        <div className={b.label}>
                            <div className={b.swatch} style={{ backgroundColor: BIOME_COLOR[c.id] ?? "#8a8f87" }} />
                            <div className={b.name}>{name(c)}</div>
                        </div>
                    </Tip>
                    <div className={b.slider}>
                        <Slider
                            value={Math.round(shares[c.id])}
                            min={0}
                            max={100}
                            step={1}
                            unit="%"
                            onPreview={(v) => act.previewValue(step.path, c.id, (v / Math.max(1, 100 - v)) * Math.max(1, total - raw[c.id]))}
                            onCommit={(v) => set(c.id, v)}
                            onEndPreview={() => act.endPreview()}
                            testId={"biomemix-slider-" + c.id}
                        />
                    </div>
                    <Tip title={t("Placemaker.UI.BiomeMix.Remove", "Take out of the mix")}>
                        <div className={classNames(b.remove, { [b.removeOff]: inMix.length <= 1 })} onClick={() => remove(c.id)} data-testid={"biomemix-remove-" + c.id}>
                            <Icon name="close" size={10} tint="dim" />
                        </div>
                    </Tip>
                </div>
            ))}
            {outMix.length ? (
                <div className={b.chips} data-testid="biomemix-add">
                    <div className={b.chipsLabel}>{t("Placemaker.UI.BiomeMix.Add", "Mix in")}</div>
                    {outMix.map((c) => (
                        <Tip key={c.id} title={name(c)} description={tip(c)}>
                            <div className={b.chip} onClick={() => add(c.id)} data-testid={"biomemix-add-" + c.id}>
                                <div className={b.swatch} style={{ backgroundColor: BIOME_COLOR[c.id] ?? "#8a8f87" }} />
                                <div className={b.chipName}>{name(c)}</div>
                            </div>
                        </Tip>
                    ))}
                </div>
            ) : null}
            {scale ? (
                <div className={classNames(b.row, b.scaleRow)} data-testid="biomemix-scale">
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
                    <div className={b.remove} />
                </div>
            ) : null}
        </div>
    );
};

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

type Part = { id: string; name: string; share: number; colour: string };

/** The mix as a bar: a part per area, dividers dragged to trade between neighbours (each keeps at least 1 %). */
const Bar = ({ parts, onCommit, t }: { parts: Part[]; onCommit: (out: Record<string, number>) => void; t: (k: string, f: string) => string }) => {
    const barRef = useRef<HTMLDivElement>(null);
    const [drag, setDrag] = useState<number[] | null>(null);
    const [width, setWidth] = useState(0);
    useLayoutEffect(() => {
        const w = barRef.current?.getBoundingClientRect().width ?? 0;
        if (Math.abs(w - width) > 1) setWidth(w);
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
    return (
        <div className={z.barBox} data-testid="biomemix-bar">
            <div className={z.bar} ref={barRef}>
                {parts.map((p, i) => {
                    const w = (shares[i] / 100) * width;
                    const text = p.name + " " + Math.round(shares[i]) + "%";
                    return shares[i] > 0 ? (
                        <Tip key={p.id} title={text}>
                            <div className={z.barPart} style={{ width: shares[i] + "%", backgroundColor: p.colour }} data-testid={"biomemix-part-" + p.id}>
                                {w > 48 ? (
                                    <div className={z.barText}>
                                        {w > 110 ? <span className={z.barName}>{p.name}</span> : null}
                                        <span className={z.barPct}>{Math.round(shares[i])}%</span>
                                    </div>
                                ) : null}
                            </div>
                        </Tip>
                    ) : null;
                })}
                {edges.map((at, i) => (
                    <div key={"edge" + i} className={z.barEdge} style={{ left: Math.min(100, at) + "%" }} onMouseDown={(e) => onDown(e, i)} data-testid={"biomemix-edge-" + i} title={t("Placemaker.UI.BiomeMix.DragTip", "Drag to share the ground")}>
                        <div className={z.barGrip} />
                    </div>
                ))}
            </div>
        </div>
    );
};
