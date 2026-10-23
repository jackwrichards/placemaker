// Stage 1 — choose the area. Area overhaul (PROTOCOL 24): three tools as one row of equal cards (icon tile + name + a
// short line): Points (draw; corners snap to road nodes, two in a row follow the road: Road nodes is folded in) · Paint
// bucket (block) · Paint area (paint + block paint: the "Smart edges" switch picks the mode). Under them ONE "Stops at"
// for every tool (roads, paths, bike paths, rail & tram, water, buildings, lots, my builds, and zoning as one choice),
// then the tool's own settings: the bucket's Gaps and Reach, Paint area's One block / Gaps / Snap / Smooth and the
// brush. Then one summary line: Area · Edge · Corners · Clear. Saved "perimeter" opens Points.
import React from "react";
import classNames from "classnames";
import { AreaMode, BrushRules, BrushShape, LocText, ToolState } from "../contract";
import { act } from "../lib/bindings";
import { area as fmtArea, group } from "../lib/format";
import { TFunc, useT } from "../lib/l10n";
import { usePanelLayout } from "../lib/layout";
import { Btn, Icon, Segmented, Switch } from "../components/common";
import { Slider } from "../controls/Slider";
import { Tip } from "../lib/tips";
import { Tile } from "./PresetBrowser";
import s from "./area.module.scss";

type Tool = "draw" | "block" | "paint";

/** The tool a mode belongs to (block paint and paint are one tool; Road nodes is part of Points). */
export const toolOf = (m: AreaMode): Tool => (m === "blockPaint" || m === "paint" ? "paint" : m === "block" ? "block" : "draw");

export function modeOptions(t: TFunc) {
    return [
        {
            value: "draw" as Tool,
            icon: "points",
            label: t("Placemaker.UI.Tool.Points", "Points"),
            line: t("Placemaker.UI.Tool.PointsLine", "Click corners"),
            description: t(
                "Placemaker.UI.Tool.PointsTip2",
                "Click corners, then the first one to close; right-click starts over. Snaps to curbs, right angles and road nodes: two corners on road nodes follow the road between them.",
            ),
        },
        {
            value: "block" as Tool,
            icon: "bucket",
            label: t("Placemaker.UI.Tool.Bucket", "Paint bucket"),
            line: t("Placemaker.UI.Tool.BucketLine", "Click to fill"),
            description: t("Placemaker.UI.Tool.BucketTip2", "Click to fill the land up to what it stops at. Shift+click adds more, right-click starts over."),
        },
        {
            value: "paint" as Tool,
            icon: "brush",
            label: t("Placemaker.UI.Tool.Paint", "Paint area"),
            line: t("Placemaker.UI.Tool.PaintLine", "Brush it on"),
            description: t("Placemaker.UI.Tool.PaintTip", "Brush the area on. Shift+drag erases, right-click starts over."),
        },
    ];
}

/** A toggle chip: icon + word, accent outline when on. */
const ToggleChip = ({ icon, label, on, disabled, tip, onClick, testId }: { icon: string; label: string; on: boolean; disabled?: boolean; tip?: string; onClick: () => void; testId: string }) => (
    <Tip title={label} description={tip}>
        <button
            type="button"
            data-testid={testId}
            aria-pressed={on && !disabled ? "true" : "false"}
            aria-disabled={disabled ? "true" : "false"}
            className={classNames(s.chip, { [s.chipOn]: on && !disabled, [s.chipOff]: disabled })}
            onClick={(e) => {
                e.stopPropagation();
                if (!disabled) onClick();
            }}
        >
            <Icon name={icon} size={12} tint={on && !disabled ? "light" : "dim"} />
            <div className={s.chipText}>{label}</div>
        </button>
    </Tip>
);

const Row = ({ label, children, off, testId, tip }: { label: string; children: React.ReactNode; off?: boolean; testId?: string; tip?: string }) => (
    <div className={classNames(s.row, { [s.rowOff]: off })} data-testid={testId} data-disabled={off ? "true" : undefined}>
        <Tip title={label} description={tip}>
            <div className={s.label}>{label}</div>
        </Tip>
        <div className={s.rowBody}>{children}</div>
    </div>
);

type StopKey = "roads" | "paths" | "bikes" | "tracks" | "water" | "buildings" | "lots" | "builds";

/** The stops are on unless the DLL says otherwise (older DLLs send no paths/bikes/tracks). */
const stopOn = (r: BrushRules, k: StopKey) => r[k] !== false;

/** Zoning as one choice; older DLLs send only the three switches. */
const zoningOf = (r: BrushRules) => (typeof r.zoning === "number" ? r.zoning : !r.zoned && !r.allZoned && !r.emptyZones ? 0 : r.emptyZones ? 3 : r.allZoned ? 2 : 1);

/**
 * Stops at — one set for every tool: what the outline never covers. Points are cut by it, the bucket's fill stops at
 * it, Paint area's brush is fenced by it (only with Smart edges on: <paramref name="off"/>).
 */
const Stops = ({ rules, off, offWhy }: { rules: BrushRules; off?: boolean; offWhy?: string }) => {
    const { t } = useT();
    const stops: { key: StopKey; icon: string; label: string; tip: string }[] = [
        { key: "roads", icon: "roads", label: t("Placemaker.UI.Rules.Roads", "Roads"), tip: t("Placemaker.UI.Rules.RoadsTip2", "Stop at the curb of every road, its junctions and turning circles.") },
        { key: "paths", icon: "footpath", label: t("Placemaker.UI.Walls.Paths", "Paths"), tip: t("Placemaker.UI.Rules.PathsTip", "Stop at footpaths.") },
        { key: "bikes", icon: "bike", label: t("Placemaker.UI.Walls.Bikes", "Bike paths"), tip: t("Placemaker.UI.Rules.BikesTip", "Stop at bike paths.") },
        { key: "tracks", icon: "track", label: t("Placemaker.UI.Walls.Tracks", "Rail & tram"), tip: t("Placemaker.UI.Walls.TracksTip", "Train and tram tracks on the ground.") },
        { key: "water", icon: "water", label: t("Placemaker.UI.Rules.Water", "Water"), tip: t("Placemaker.UI.Rules.WaterTip", "Keep off deep water.") },
        { key: "buildings", icon: "building", label: t("Placemaker.UI.Rules.Buildings", "Buildings"), tip: t("Placemaker.UI.Rules.BuildingsTip", "Keep off building lots.") },
        { key: "lots", icon: "area", label: t("Placemaker.UI.Rules.Lots", "Lots"), tip: t("Placemaker.UI.Rules.LotsTip2", "Keep off other lots (other mods, extractors).") },
        { key: "builds", icon: "otherBuilds", label: t("Placemaker.UI.Rules.MyBuilds", "My builds"), tip: t("Placemaker.UI.Rules.BuildsTip", "Keep off what you built with Placemaker.") },
    ];
    const zoning = zoningOf(rules);
    const allOn = stops.every((x) => stopOn(rules, x.key));
    const setAll = (on: boolean) => {
        const change: Partial<Record<StopKey, boolean>> = {};
        for (const x of stops) change[x.key] = on;
        act.setBrushRules(change);
    };
    return (
        <>
            <Row label={t("Placemaker.UI.Rules.Stops", "Stops at")} off={off} testId="area-stops" tip={t("Placemaker.UI.Rules.StopsTip", "What the area never covers, for every tool. Red on the map.")}>
                <div className={s.chips} data-testid="area-stops-chips">
                    <Tip
                        title={t("Placemaker.UI.Rules.AllStops", "All of them")}
                        description={off ? offWhy : t("Placemaker.UI.Rules.AllStopsTip", "Lit when it stops at everything here. Click to turn them all on, or all off when they are all on.")}
                    >
                        <button
                            type="button"
                            data-testid="stop-all"
                            aria-pressed={allOn && !off ? "true" : "false"}
                            aria-disabled={off ? "true" : "false"}
                            className={classNames(s.chip, s.chipStar, { [s.chipOn]: allOn && !off, [s.chipOff]: off })}
                            onClick={(e) => {
                                e.stopPropagation();
                                if (!off) setAll(!allOn);
                            }}
                        >
                            <Icon name="star" size={12} tint={allOn && !off ? "light" : "dim"} />
                        </button>
                    </Tip>
                    {stops.map((x) => (
                        <ToggleChip
                            key={x.key}
                            icon={x.icon}
                            label={x.label}
                            on={stopOn(rules, x.key)}
                            disabled={off}
                            tip={off ? offWhy : x.tip}
                            onClick={() => act.setBrushRules({ [x.key]: !stopOn(rules, x.key) })}
                            testId={`stop-${x.key}`}
                        />
                    ))}
                </div>
            </Row>
            <Row label={t("Placemaker.UI.Rules.Zoning", "Zoning")} off={off} testId="area-zoning" tip={t("Placemaker.UI.Rules.ZoningTip", "How much of the zoning the area keeps off.")}>
                <Segmented
                    testId="stop-zoning"
                    stretch
                    value={String(zoning)}
                    onChange={(v) => !off && act.setBrushRules({ zoning: Number(v) })}
                    options={[
                        { value: "0", label: t("Placemaker.UI.Rules.ZoningOff", "Ignore"), description: t("Placemaker.UI.Rules.ZoningOffTip", "Zoning does not stop it.") },
                        { value: "1", label: t("Placemaker.UI.Rules.ZoningBuilt", "Built"), description: t("Placemaker.UI.Rules.ZoningBuiltTip", "Keep off zoned cells that have a building.") },
                        { value: "2", label: t("Placemaker.UI.Rules.ZoningZoned", "Zoned"), description: t("Placemaker.UI.Rules.ZoningZonedTip", "Keep off every zoned cell, built on or not.") },
                        { value: "3", label: t("Placemaker.UI.Rules.ZoningGrid", "Grid"), description: t("Placemaker.UI.Rules.ZoningGridTip", "Keep off the whole zone grid along the streets, even cells nobody zoned.") },
                    ]}
                />
            </Row>
        </>
    );
};

const GapSlider = ({ rules, off }: { rules: BrushRules; off?: boolean }) => {
    const { t } = useT();
    return (
        <Row
            label={t("Placemaker.UI.Rules.Gap", "Gaps")}
            off={off}
            testId="area-gap"
            tip={t("Placemaker.UI.Rules.GapTip", "Openings narrower than this count as closed, and places narrower than it are left out. 0 = off.")}
        >
            <Slider testId="area-gap-slider" value={rules.gap ?? 10} min={0} max={30} step={1} unit="m" onPreview={() => undefined} onCommit={(v) => !off && act.setBrushRules({ gap: v })} />
        </Row>
    );
};

/** Paint bucket: the stops, then how it fills (gaps, reach). */
const BucketSettings = ({ rules }: { rules: BrushRules }) => {
    const { t } = useT();
    return (
        <div className={s.settings} data-testid="bucket-settings">
            <Stops rules={rules} />
            <div className={s.rule} />
            <GapSlider rules={rules} />
            <Row label={t("Placemaker.UI.Rules.Reach", "Reach")} testId="area-reach" tip={t("Placemaker.UI.Rules.ReachTip", "How far the fill may spread from where you click. In open land it fills a circle this size.")}>
                <Slider testId="area-reach-slider" value={rules.reach ?? 500} min={20} max={1500} step={10} unit="m" onPreview={() => undefined} onCommit={(v) => act.setBrushRules({ reach: v })} />
            </Row>
        </div>
    );
};

/** Points: just the points (no stops: the outline is exactly what was drawn). */
const PointsSettings = () => {
    const { t } = useT();
    return (
        <div className={s.settings} data-testid="points-settings">
            <div className={s.note} data-testid="points-note">
                <Icon name="info" size={11} tint="dim" />
                <div className={s.noteText}>{t("Placemaker.UI.Area.PointsNodes", "Over a junction a corner snaps to the road node; two in a row follow the road. Alt: no snapping.")}</div>
            </div>
        </div>
    );
};

/** Paint area: Smart edges + the stops, one block, gaps, snap, smooth; then the brush. */
const PaintSettings = ({ state, rules }: { state: ToolState; rules: BrushRules }) => {
    const { t, tl } = useT();
    // Smart edges = the block paint mode; a DLL without it greys the switch with the reason (s15).
    const smartAv = modeAvailability(state, "blockPaint", t, tl);
    const b = state.brush;
    const smart = state.areaMode === "blockPaint";
    const offWhy = t("Placemaker.UI.Why.SmartOff", "Smart edges is off");
    const oneBlock = smart && rules.stayInBlock !== false;
    const setSmart = (on: boolean) => {
        if (on && !smartAv.enabled) return;
        act.setBrushRules({ anything: !on });
        act.setAreaMode(on ? "blockPaint" : "paint");
    };
    return (
        <div className={s.settings} data-testid="brush">
            <div className={s.smartRow}>
                <Tip
                    title={t("Placemaker.UI.Rules.Smart", "Smart edges")}
                    description={t("Placemaker.UI.Rules.SmartTip", "On: the brush stops at what you switch on below. Off: it paints over everything.")}
                >
                    <div className={s.smartLabel}>{t("Placemaker.UI.Rules.Smart", "Smart edges")}</div>
                </Tip>
                <Tip title={t("Placemaker.UI.Rules.Smart", "Smart edges")} description={smartAv.enabled ? undefined : smartAv.reason}>
                    <div className={smartAv.enabled ? s.switchCell : classNames(s.switchCell, s.rowOff)} data-testid="smart-edges-wrap" data-disabled={smartAv.enabled ? undefined : "true"} data-reason={smartAv.reason}>
                        <Switch on={smart} onChange={setSmart} testId="smart-edges" />
                    </div>
                </Tip>
                <div className={s.flex} />
                <Btn icon="reset" iconSize={11} title={t("Placemaker.UI.Rules.Reset", "Reset edges")} onClick={act.resetBrushRules} testId="brush-rules-reset" />
            </div>
            <Stops rules={rules} off={!smart} offWhy={offWhy} />
            <Row label={t("Placemaker.UI.Rules.Block", "One block")} off={!smart} tip={t("Placemaker.UI.Rules.BlockTip2", "A stroke stays in what the Paint bucket would fill where it started.")}>
                <div className={s.switchCell}>
                    <Switch on={oneBlock} onChange={(v) => smart && act.setBrushRules({ stayInBlock: v })} testId="brush-rule-stayInBlock" />
                </div>
            </Row>
            <GapSlider rules={rules} off={!oneBlock} />
            <Row label={t("Placemaker.UI.Rules.Snap", "Snap")} off={!smart} tip={t("Placemaker.UI.Rules.SnapTip", "Pulls painted edges this close onto a curb.")}>
                <Slider testId="brush-rule-snap" value={rules.snap} min={0} max={20} step={1} unit="m" onPreview={() => undefined} onCommit={(v) => smart && act.setBrushRules({ snap: v })} />
            </Row>
            <Row label={t("Placemaker.UI.Rules.Smooth", "Smooth")} off={!smart} tip={t("Placemaker.UI.Rules.SmoothTip", "How smooth the painted edge is.")}>
                <Slider testId="brush-rule-smoothing" value={rules.smoothing} min={0} max={6} step={1} onPreview={() => undefined} onCommit={(v) => smart && act.setBrushRules({ smoothing: v })} />
            </Row>
            <div className={s.rule} />
            <Row label={t("Placemaker.UI.Brush.Shape", "Shape")}>
                <Segmented
                    testId="brush-shape"
                    stretch
                    value={b.shape}
                    onChange={(v) => act.setBrush(v as BrushShape, b.size, b.angle)}
                    options={[
                        { value: "round", icon: "brushRound", label: t("Placemaker.UI.Brush.Round", "Round") },
                        { value: "square", icon: "brushSquare", label: t("Placemaker.UI.Brush.Square", "Square") },
                    ]}
                />
            </Row>
            <Row label={t("Placemaker.UI.Brush.Size", "Size")}>
                <Slider testId="brush-size" value={b.size} min={5} max={200} step={1} unit="m" onPreview={(v) => act.setBrush(b.shape, v, b.angle)} onCommit={(v) => act.setBrush(b.shape, v, b.angle)} />
            </Row>
            {b.shape === "square" ? (
                <Row label={t("Placemaker.UI.Brush.Angle", "Angle")} testId="brush-angle-row">
                    <Slider testId="brush-angle" value={b.angle} min={0} max={90} step={1} unit="deg" onPreview={(v) => act.setBrush(b.shape, b.size, v)} onCommit={(v) => act.setBrush(b.shape, b.size, v)} />
                </Row>
            ) : null}
        </div>
    );
};

const HOWTO: Record<Tool, [string, string]> = {
    draw: ["Placemaker.UI.Area.HowDraw", "Click corners, then the first one"],
    block: ["Placemaker.UI.Area.HowBlock2", "Click to fill"],
    paint: ["Placemaker.UI.Area.HowPaint", "Drag to paint, Shift+drag erases"],
};

/**
 * (s15) Can this mode be used? The DLL lists the modes it handles (state.areaModes). An older DLL sends no list:
 * Block paint arrived together with the brush rules (s11), so without rules the DLL cannot do it — greyed with
 * "restart" instead of silently doing nothing (the UI file reloads without a restart, the DLL does not).
 */
export function modeAvailability(state: ToolState, mode: string, t: TFunc, tl: (l: LocText | undefined, f?: string) => string): { enabled: boolean; reason?: string } {
    const restart = t("Placemaker.UI.Why.RestartForMode", "Restart the game to use this: Placemaker was updated while the game was running.");
    if (state.areaModes) {
        const m = state.areaModes.find((x) => x.mode === mode);
        if (!m) return { enabled: false, reason: restart };
        return m.enabled ? { enabled: true } : { enabled: false, reason: tl(m.reason, t("Placemaker.UI.Why.NotNow", "Not available with the current settings")) };
    }
    if (mode === "blockPaint" && !state.brush.rules) return { enabled: false, reason: restart };
    return { enabled: true };
}

export const AreaStage = ({ state }: { state: ToolState }) => {
    const { t, tl } = useT();
    const layout = usePanelLayout();
    const rules = state.brush.rules;
    const a = state.area;
    const tool = toolOf(state.areaMode);
    // Paint area opens in Smart edges (block paint) unless the DLL cannot, then plain paint.
    const paintMode = (): AreaMode => (modeAvailability(state, "blockPaint", t, tl).enabled && !rules?.anything ? "blockPaint" : "paint");
    const cols = layout.width >= 360 ? 3 : 1;
    return (
        <div className={s.stage} data-testid="stage-area">
            <div className={s.tools} data-testid="area-mode">
                {modeOptions(t).map((o) => {
                    const on = o.value === tool;
                    const mode: AreaMode = o.value === "paint" ? (on ? state.areaMode : paintMode()) : o.value;
                    const av = modeAvailability(state, o.value === "paint" ? "paint" : o.value, t, tl);
                    return (
                        <div key={o.value} className={s.toolCell} style={{ width: 100 / cols + "%" }}>
                            <Tip title={o.label} description={av.enabled ? o.description : av.reason + " — " + o.description}>
                                <button
                                    type="button"
                                    data-testid={`area-mode-${o.value}`}
                                    aria-pressed={on ? "true" : "false"}
                                    aria-disabled={av.enabled ? undefined : "true"}
                                    data-reason={av.enabled ? undefined : av.reason}
                                    className={classNames(s.tool, { [s.toolOn]: on, [s.toolOff]: !av.enabled && !on })}
                                    onClick={(e) => {
                                        e.stopPropagation();
                                        if ((!on || state.areaMode === "perimeter") && av.enabled) act.setAreaMode(mode);
                                    }}
                                >
                                    <Tile icon={o.icon} color={on ? "#8fd1ff" : "#9fb0b9"} size={20} />
                                    <div className={s.toolText}>
                                        <div className={s.toolName}>{o.label}</div>
                                        <div className={s.toolLine}>{o.line}</div>
                                    </div>
                                </button>
                            </Tip>
                        </div>
                    );
                })}
            </div>
            {tool === "paint" && rules ? <PaintSettings state={state} rules={rules} /> : null}
            {tool === "paint" && !rules ? (
                <div className={s.settings} data-testid="brush">
                    <Row label={t("Placemaker.UI.Brush.Size", "Size")}>
                        <Slider testId="brush-size" value={state.brush.size} min={5} max={200} step={1} unit="m" onPreview={(v) => act.setBrush(state.brush.shape, v, state.brush.angle)} onCommit={(v) => act.setBrush(state.brush.shape, v, state.brush.angle)} />
                    </Row>
                </div>
            ) : null}
            {tool === "block" && rules ? <BucketSettings rules={rules} /> : null}
            {tool === "draw" ? <PointsSettings /> : null}
            <div className={s.summary} data-testid="area-summary">
                {a.closed && a.areaM2 > 0 ? (
                    <>
                        <div className={s.sumLabel}>{t("Placemaker.UI.Area.Selected", "Area")}</div>
                        <div className={s.sumValue}>{fmtArea(a.areaM2)}</div>
                        <div className={s.sumLabel}>{t("Placemaker.UI.Area.Around", "Edge")}</div>
                        <div className={s.sumValue}>{group(a.perimeterM) + " m"}</div>
                        <div className={s.sumLabel}>{t("Placemaker.UI.Area.CornersLabel", "Corners")}</div>
                        <div className={s.sumValue}>{group(a.pointCount)}</div>
                        <div className={s.sumHint} data-testid="area-hint">
                            {t("Placemaker.UI.Area.Adjust", "drag corners anytime")}
                        </div>
                    </>
                ) : (
                    <>
                        <Icon name="info" size={12} tint="accent" />
                        <div className={s.sumHint} data-testid="area-hint">
                            {t(HOWTO[tool][0], HOWTO[tool][1])}
                        </div>
                    </>
                )}
                <div className={s.flex} />
                {a.pointCount > 0 || a.closed ? <Btn icon="trash" iconSize={11} label={t("Placemaker.UI.Area.Clear", "Clear")} onClick={act.clearArea} testId="clear-area" className={s.clear} /> : null}
            </div>
        </div>
    );
};
