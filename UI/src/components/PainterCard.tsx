// (PROTOCOL 25) Outgrow's panels. The Outgrow tool's step 2 (PolishDesign): what the area holds, one row of outgrow types
// ("looks" in the code; By zone first, each in its colour) and the editor of the one picked (LookEditor: Save, Copy as new,
// Back to saved, Back to as it ships, Rename, Delete; colour, planting style, then every setting, grouped and folded: what
// the land past the lot becomes and the water gap first). Save on a built-in type changes it everywhere it is used.
// The Global outgrow window (PolishCityCard, beside the panel, its header button, there whatever is picked): Outgrow all /
// Remove all outgrow, Outgrow new buildings (this city), the status strip, the brush and the map lens side by side, then
// Zones (one line per zone type: outgrown / buildings, its outgrow type, Edit; per kind and per zone Outgrow / Remove),
// Edit (the editor) and Numbers (OutgrowStats). A Zoning type's zone has its Outgrow on its line (ZoneOutgrowInline:
// switch, outgrow type, Edit; the list under the line: ZoneOutgrowPick); Edit opens Global outgrow's Edit on it.
import React, { ReactNode, useCallback, useEffect, useState } from "react";
import classNames from "classnames";
import { PainterState, PolishZone, ToolState } from "../contract";
import { humanize, TFunc, useT } from "../lib/l10n";
import { Tip } from "../lib/tips";
import { Btn, Collapsible, Icon, Segmented, Switch } from "./common";
import { Slider } from "../controls/Slider";
import { Confirm } from "./dialog";
import { act } from "../lib/bindings";
import { categoryColor, CUSTOM_COLOR } from "../lib/palette";
import { Chip } from "../stages/PresetBrowser";
import { LensLegend, NOT_OUTGROWN, OutgrowStats, StatusStrip } from "./OutgrowStats";
import { ScrollArea } from "./ScrollArea";
import b from "../stages/browser.module.scss";
import d from "../stages/design.module.scss";
import c from "../controls/controls.module.scss";
import s from "./painter.module.scss";
import pn from "./panel.module.scss";

export const THEME_ICON: Record<string, string> = { zone: "zone", garden: "flower", natural: "rocks", tidy: "topiary", yard: "crate" };
const THEMES = ["garden", "natural", "tidy", "yard"];

/** A look's name: the player's own as they named it, a built-in one by its key. */
export const lookName = (t: TFunc, p: PainterState | undefined, id: string) => {
    if (id === "zone") return t("Placemaker.UI.Painter.Theme.zone", "By zone");
    if (id === "off") return t("Placemaker.UI.Polish.KindOff", "Off");
    if (id === KIND_AUTO) return t("Placemaker.UI.Polish.KindAuto", "By zone type");
    const l = p?.looks?.find((x) => x.id === id);
    if (!l) return humanize(id);
    return l.custom ? l.name : t(l.name, humanize(l.id));
};

type Knob = { id: string; en: string; tip: string; kind: "toggle" | "slider" | "ground" | "choice"; min?: number; max?: number; step?: number; unit?: string; options?: { value: number; en: string; icon?: string }[] };

/** The ground a paint job uses (the map's terrain materials). */
const GROUNDS = [
    { value: 0, en: "None" },
    { value: 1, en: "Dry" },
    { value: 2, en: "Lush" },
    { value: 3, en: "Dirt" },
    { value: 4, en: "Sand" },
];

/** A kind's look "by zone type": each zone type its own (NeighbourPolishSystem.KindAuto). */
const KIND_AUTO = "auto";
/** The built-in looks made for zone types (Core/Neighbours/PolishLooks.ZoneLookIds), in order. */
const ZONE_LOOKS = ["houses", "rows", "apartments", "towers", "mixed", "shops", "bigShops", "offices", "officeTowers", "industry"];
/** Built-in looks with an icon of their own (else their base theme's). */
const LOOK_ICON: Record<string, string> = {
    meadow: "leaf", rustic: "pickaxe", coastal: "waves", manicured: "topiary",
    houses: "people", rows: "block", apartments: "building", towers: "column", mixed: "awning",
    shops: "shop", bigShops: "bag", offices: "board", officeTowers: "obelisk", industry: "factory",
};
const GROUPS: { id: string; en: string; icon: string; open?: boolean; knobs: Knob[] }[] = [
    {
        id: "land", en: "What the land becomes", icon: "layers", open: true,
        knobs: [
            {
                id: "fill", en: "Beyond the lot", tip: "What a building's land past its own lot becomes, whatever the building is: a garden (lawn, fences, beds), a car park yard (green edges, bins, marked bays with parked cars), or paving. The outgrow type alone decides: whatever the building is (a house given a car park type gets a car park).", kind: "choice",
                options: [{ value: 1, en: "Garden" }, { value: 2, en: "Car park" }, { value: 3, en: "Paved" }],
            },
            {
                id: "parkStyle", en: "Car park style", tip: "How a car park yard looks. Clean: a wide grass edge, trees in planters at the ends of the rows, a tidy bin corner. Everyday: grass edges, bins and stock, most bays taken. Industrial: concrete, no grass, more vans, pallets, barrels, skips and containers, dry worn ground further out.", kind: "choice",
                options: [{ value: 1, en: "Clean" }, { value: 2, en: "Everyday" }, { value: 3, en: "Industrial" }],
            },
            { id: "parkReach", en: "Car park reach", tip: "How far out from the building its car park yard is paved and parked. Further out, the land is the outgrow type's own: its ground, patches, rocks and things.", kind: "slider", min: 0, max: 300, step: 5, unit: "m" },
            { id: "parkCars", en: "Parked cars", tip: "The share of parking bays with a car or van in them.", kind: "slider", min: 0, max: 100, step: 5, unit: "%" },
            { id: "parkWorn", en: "Worn edge", tip: "The chance a car park's edge is worn, as the strip malls' are: the ground painted, dirt fading into dry grass out from the paving, no grass strip. Otherwise a grass strip with trees. Industrial car parks are always worn.", kind: "slider", min: 0, max: 100, step: 5, unit: "%" },
            { id: "waterGap", en: "Water gap", tip: "How far the land keeps from water: gardens, yards and car parks stop this far from the shore, along a smooth line. The building's own lot is never cut.", kind: "slider", min: 0, max: 200, step: 1, unit: "m" },
        ],
    },
    {
        id: "reach", en: "Reach and joining", icon: "ruler", open: true,
        knobs: [
            { id: "sideReach", en: "Side reach", tip: "How far a garden reaches sideways to meet the neighbour beside it. Very large values make the polish slower in big cities.", kind: "slider", min: 0, max: 500, step: 1, unit: "m" },
            { id: "maxFrontGap", en: "Street gap", tip: "Two neighbours this far apart at the street still join (one shared fence or hedge).", kind: "slider", min: 0, max: 500, step: 1, unit: "m" },
            { id: "maxBackGap", en: "Back gap", tip: "How far apart their back corners may be (wedges on the outside of a curve).", kind: "slider", min: 0, max: 500, step: 1, unit: "m" },
            { id: "maxRearGap", en: "Back to back", tip: "Two gardens back to back this far apart meet in the middle.", kind: "slider", min: 0, max: 500, step: 1, unit: "m" },
            { id: "maxFacingGap", en: "Block corners", tip: "Two lots this far apart meet halfway, at any angle (a block's corners, two streets that meet): the land between them is shared on one line.", kind: "slider", min: 0, max: 500, step: 1, unit: "m" },
            { id: "maxDeepen", en: "Line up backs", tip: "A shallow garden reaches back to its row's deepest by up to this much.", kind: "slider", min: 0, max: 500, step: 1, unit: "m" },
            { id: "hug", en: "Reach out to roads and buildings", tip: "A side or back nobody else has grows out to a road, path or building near it and hugs it.", kind: "toggle" },
            { id: "hugReach", en: "Reach out", tip: "How far a garden reaches out to a road, path or building. Very large values make the polish slower in big cities.", kind: "slider", min: 0, max: 500, step: 1, unit: "m" },
            { id: "openReach", en: "Into open land", tip: "A side or back with nothing near it to meet still grows this far into open land (a car park or paving: only its back). Very large values fill whole fields (and make the polish slower in big cities).", kind: "slider", min: 0, max: 500, step: 1, unit: "m" },
            { id: "wrap", en: "Meet behind a shallow lot", tip: "Two deep gardens meet behind a shallow one between them.", kind: "toggle" },
            { id: "closeBacks", en: "Close open backs", tip: "A garden open at the back closes with a fence or hedge the joining freed.", kind: "toggle" },
        ],
    },
    {
        id: "fences", en: "Fences", icon: "edgeZoning",
        knobs: [
            { id: "closeGardens", en: "Close every garden", tip: "Every open edge of a garden (not its street front) gets fence or hedge pieces, following its bends and the roads it hugs.", kind: "toggle" },
            {
                id: "fenceKind", en: "New fences", tip: "What closes the open edges: the house's own fence or hedge kind, or one for every house.", kind: "choice",
                options: [{ value: 0, en: "Own" }, { value: 1, en: "Low hedge" }, { value: 2, en: "Tall hedge" }, { value: 3, en: "Low fence" }, { value: 4, en: "Tall fence" }],
            },
        ],
    },
    {
        id: "lawns", en: "Lawns and ground paint", icon: "leaf",
        knobs: [
            { id: "lawns", en: "Lawns up to the fences", tip: "A garden's lawn grows to its new fences, around driveways and paving.", kind: "toggle" },
            { id: "lawnReach", en: "Lawn near the house", tip: "Mowed lawn this far from the house; a big garden is painted further out (Ground: further out). 400 or more: lawn everywhere.", kind: "slider", min: 0, max: 500, step: 1, unit: "m" },
            { id: "paintedGardens", en: "Painted gardens", tip: "How many gardens are painted (lush ground, soft edges) instead of keeping their lawns. Natural and Yard more often, Tidy seldom.", kind: "slider", min: 0, max: 100, step: 5, unit: "%" },
            { id: "gardenPaint", en: "Garden paint", tip: "How strong the paint of a painted garden is.", kind: "slider", min: 0, max: 1, step: 0.05 },
            { id: "paint", en: "Ground paint", tip: "Soft lush ground past the gardens and worn ground along fences facing open land.", kind: "toggle" },
            { id: "blendWidth", en: "Paint reach", tip: "How far the lush ground reaches past the gardens.", kind: "slider", min: 0, max: 150, step: 1, unit: "m" },
            { id: "blendFar", en: "Faint reach", tip: "How far the fainter, wider band reaches.", kind: "slider", min: 0, max: 250, step: 1, unit: "m" },
            { id: "blendStrength", en: "Paint strength", tip: "How strong the paint past the gardens is.", kind: "slider", min: 0, max: 1, step: 0.05 },
        ],
    },
    {
        id: "ground", en: "Ground", icon: "paint",
        knobs: [
            { id: "gardenLayer", en: "Painted gardens", tip: "The ground a painted garden is painted with.", kind: "ground" },
            { id: "meadowLayer", en: "Further out", tip: "The ground of a big garden beyond its lawn near the house.", kind: "ground" },
            { id: "meadowStrength", en: "Further out strength", tip: "How strong that paint is.", kind: "slider", min: 0, max: 1, step: 0.05 },
            { id: "blendLayer", en: "Just past the gardens", tip: "The ground of the band just past the gardens.", kind: "ground" },
            { id: "blendFarLayer", en: "Fainter band", tip: "The ground of the wider, fainter band further out.", kind: "ground" },
            { id: "edgeLayer", en: "Along open fences", tip: "Worn ground along back fences that face open land.", kind: "ground" },
            { id: "edgeWidth", en: "Fence band width", tip: "How wide the worn band along those fences is.", kind: "slider", min: 0, max: 15, step: 0.5, unit: "m" },
            { id: "edgeStrength", en: "Fence band strength", tip: "How strong it is.", kind: "slider", min: 0, max: 1, step: 0.05 },
            { id: "roadLayer", en: "Along roads", tip: "A band along the roads and paths beside the gardens (sand or dirt by the road…).", kind: "ground" },
            { id: "roadWidth", en: "Road band width", tip: "How wide the band along roads and paths is.", kind: "slider", min: 0, max: 20, step: 0.5, unit: "m" },
            { id: "roadStrength", en: "Road band strength", tip: "How strong it is.", kind: "slider", min: 0, max: 1, step: 0.05 },
            { id: "rockLayer", en: "Under rocks", tip: "The ground under rock gardens and outcrops.", kind: "ground" },
            { id: "rockGround", en: "Around outcrops", tip: "The wider ground around an outcrop.", kind: "ground" },
            { id: "yardLayer", en: "Storage yards", tip: "The ground of a storage yard.", kind: "ground" },
            { id: "patchLayer", en: "Worn patches", tip: "The ground of the worn patches in big gardens.", kind: "ground" },
            { id: "patchChance", en: "Patch chance", tip: "How many worn patches big gardens get.", kind: "slider", min: 0, max: 100, step: 5, unit: "%" },
        ],
    },
    {
        id: "planting", en: "Planting and features", icon: "shrub",
        knobs: [
            { id: "plant", en: "Beds and bushes", tip: "Planted beds with bushes along new fences. Off: no planting, features, paths or rocks.", kind: "toggle" },
            { id: "bedChance", en: "Beds", tip: "How often a new fence gets a bed with bushes.", kind: "slider", min: 0, max: 100, step: 5, unit: "%" },
            { id: "bushesPerGarden", en: "Bushes", tip: "The most bushes the beds of one garden get.", kind: "slider", min: 0, max: 80, step: 1, unit: "count" },
            { id: "features", en: "Garden features", tip: "Big new garden land gets something by its theme: a rock garden, a sand pit, a log pile, a bench nook, a flower garden, a storage yard.", kind: "toggle" },
            { id: "featureChance", en: "Feature chance", tip: "How many big gardens get a feature.", kind: "slider", min: 0, max: 100, step: 5, unit: "%" },
            { id: "featureSpacing", en: "More features every", tip: "A big garden gets one more feature for every this much new land (at most 6).", kind: "slider", min: 50, max: 3000, step: 50, unit: "m2" },
            { id: "featureMinLand", en: "Feature land", tip: "New garden land a feature needs.", kind: "slider", min: 10, max: 600, step: 10, unit: "m2" },
            { id: "rockChance", en: "Rocks", tip: "How often a rock sits where two gardens meet at the street (never against a hedge).", kind: "slider", min: 0, max: 100, step: 5, unit: "%" },
        ],
    },
    {
        id: "paths", en: "Paths", icon: "footpath",
        knobs: [
            { id: "pathChance", en: "Garden paths", tip: "How many big gardens get a paved path from the house to the back.", kind: "slider", min: 0, max: 100, step: 5, unit: "%" },
            { id: "alleys", en: "Walkways between houses", tip: "A narrow even gap between two houses becomes a paved walkway.", kind: "toggle" },
            { id: "alleyChance", en: "Walkways", tip: "How often a narrow gap becomes a walkway.", kind: "slider", min: 0, max: 100, step: 5, unit: "%" },
        ],
    },
];

const changedIn = (knobs: Knob[], p: PainterState) => knobs.filter((k) => Math.abs((p.values?.[k.id] ?? 0) - (p.lookValues?.[k.id] ?? 0)) > 1e-4).length;

/** One options row, as Controls.tsx draws a type's: label (tooltip) left, editor right, reset when changed. */
const Row = ({ id, label, tip, toggle, changed, onReset, children }: { id: string; label: string; tip?: string; toggle?: boolean; changed?: boolean; onReset?: () => void; children: ReactNode }) => {
    const { t } = useT();
    return (
        <div className={c.row} data-testid={"polish-row-" + id}>
            <div className={c.rowHead}>
                <Tip title={label} description={tip || undefined}>
                    <div className={classNames(c.label, { [c.labelWide]: toggle })}>
                        <div className={c.labelText}>{label}</div>
                    </div>
                </Tip>
                <div className={classNames(c.editor, { [c.editorTight]: toggle })}>{children}</div>
                <div className={c.affordances}>
                    <div className={c.miniSpacer} />
                    {changed && onReset ? (
                        <Btn icon="reset" iconSize={11} className={c.mini} testId={"polish-reset-" + id} title={t("Placemaker.UI.ResetValue", "Reset to default")} onClick={onReset} />
                    ) : (
                        <div className={c.miniSpacer} />
                    )}
                </div>
            </div>
        </div>
    );
};

const KnobRow = ({ k, p }: { k: Knob; p: PainterState }) => {
    const { t } = useT();
    const v = p.values?.[k.id] ?? 0;
    const def = p.lookValues?.[k.id] ?? v;
    const changed = Math.abs(v - def) > 1e-4;
    const label = t("Placemaker.UI.Polish.Knob." + k.id, k.en);
    const tip = t("Placemaker.UI.Polish.Knob." + k.id + ".Tip", k.tip);
    const set = (x: number) => act.polishKnob(k.id, x);
    if (k.kind === "ground" || k.kind === "choice") {
        const opts = k.kind === "ground" ? GROUNDS : k.options ?? [];
        return (
            <Row id={k.id} label={label} tip={tip} changed={changed} onReset={() => set(def)}>
                <Segmented
                    testId={"polish-" + k.id}
                    stretch
                    value={String(Math.round(v))}
                    onChange={(x) => set(Number(x))}
                    options={opts.map((o) => ({ value: String(o.value), label: t("Placemaker.UI.Polish.Opt." + k.id + "." + o.value, k.kind === "ground" ? t("Placemaker.UI.Polish.Ground." + o.value, o.en) : o.en) }))}
                />
            </Row>
        );
    }
    if (k.kind === "toggle")
        return (
            <Row id={k.id} label={label} tip={tip} toggle changed={changed} onReset={() => set(def)}>
                <div className={c.toggleCell}>
                    <Switch testId={"polish-" + k.id} on={v > 0.5} onChange={(on) => set(on ? 1 : 0)} />
                </div>
            </Row>
        );
    return (
        <Row id={k.id} label={label} tip={tip} changed={changed} onReset={() => set(def)}>
            <Slider testId={"polish-" + k.id} value={v} min={k.min ?? 0} max={k.max ?? 100} step={k.step ?? 1} unit={k.unit} onPreview={() => {}} onCommit={set} />
        </Row>
    );
};

/** A look's every value: the defaults, then its own settings (as the panel was sent them). */
export const lookValuesOf = (p: PainterState, id: string): Record<string, number> => {
    const l = p.looks?.find((x) => x.id === id);
    return { ...(p.defaults ?? {}), ...(l?.knobs ?? {}) };
};

/** An outgrow type's colour (the map and the charts). */
export const lookColor = (p: PainterState | undefined, id: string) => p?.looks?.find((x) => x.id === id)?.color || "#8a93a3";

// Opening the editor from anywhere (a zone's dropdown in Zoning, a zone card): the panel opens Global outgrow, which shows
// its Edit tab with that outgrow type picked (a request made before the window is open waits for it).
const lookEditorListeners = new Set<() => void>();
let pendingLook: string | null = null;
export const openLookEditor = (id: string) => {
    pendingLook = id;
    lookEditorListeners.forEach((l) => l());
};
export const useLookEditorRequests = (onRequest: () => void) => {
    useEffect(() => {
        lookEditorListeners.add(onRequest);
        return () => {
            lookEditorListeners.delete(onRequest);
        };
    }, [onRequest]);
};

/** Every outgrow type in one list: the ones made for zone types first, then the others, then the player's own. */
const orderedLooks = (p: PainterState) => {
    const looks = p.looks ?? [];
    return [
        ...ZONE_LOOKS.map((id) => looks.find((l) => l.id === id)).filter((l): l is NonNullable<typeof l> => !!l),
        ...looks.filter((l) => !l.custom && !ZONE_LOOKS.includes(l.id)),
        ...looks.filter((l) => l.custom),
    ];
};

/** The outgrow type's buttons: Save (over it: the player's own, or a built-in one, which then is what it is everywhere),
 *  Copy as new, Back to saved, Back to how a built-in one ships, Rename, Delete. A name field when one is asked for. */
const LookBar = ({ p }: { p: PainterState }) => {
    const { t } = useT();
    const [naming, setNaming] = useState<"saveAs" | "rename" | null>(null);
    const [name, setName] = useState("");
    const [asking, setAsking] = useState(false);
    // Save and Back to as it ships change every building outgrown with this type (they are outgrown again with it, a few
    // at a time): asked first, once
    const [applying, setApplying] = useState<"save" | "reset" | null>(null);
    const look = p.looks?.find((x) => x.id === p.theme);
    if (!look) return null;
    const using = p.stats?.looks?.[look.id] ?? 0;
    const apply = (what: "save" | "reset") => (using > 0 ? setApplying(what) : act.polishLook(what));
    const submit = () => {
        if (naming && name.trim()) act.polishLook(naming, name.trim());
        setNaming(null);
    };
    if (naming)
        return (
            <div className={s.lookBar} data-testid="polish-look-name">
                <input
                    className={b.searchInput}
                    data-testid="polish-look-name-input"
                    autoFocus
                    value={name}
                    placeholder={t("Placemaker.UI.Polish.LookName2", "Name of the outgrow type")}
                    onChange={(e) => setName(e.target.value)}
                    onKeyDown={(e) => {
                        if (e.key === "Enter") submit();
                        else if (e.key === "Escape") setNaming(null);
                        e.stopPropagation();
                    }}
                />
                <Btn variant="accent" label={t("Placemaker.UI.Polish.SaveName", "Save")} onClick={submit} testId="polish-look-name-ok" />
                <Btn label={t("Placemaker.UI.Cancel", "Cancel")} onClick={() => setNaming(null)} testId="polish-look-name-cancel" />
            </div>
        );
    const display = lookName(t, p, look.id);
    return (
        <div className={s.lookBar} data-testid="polish-look-bar">
            {p.modified ? (
                <div className={s.modified}>{t("Placemaker.UI.Polish.Changed", "Changed")}</div>
            ) : look.edited ? (
                <div className={s.modified}>{t("Placemaker.UI.Polish.Edited", "Your version")}</div>
            ) : null}
            <div className={s.flex} />
            {p.modified ? (
                <Btn
                    variant="accent"
                    icon="save"
                    iconSize={12}
                    label={t("Placemaker.UI.Polish.Save", "Save")}
                    title={look.custom ? t("Placemaker.UI.Polish.Save", "Save") : t("Placemaker.UI.Polish.SaveBuiltIn", "Save: this is what {0} is from now on, everywhere it is used", [display])}
                    onClick={() => apply("save")}
                    testId="polish-look-save"
                />
            ) : null}
            <Btn
                icon="plus"
                iconSize={12}
                label={t("Placemaker.UI.Polish.CopyAsNew", "Copy as new type")}
                onClick={() => {
                    setName(display + " 2");
                    setNaming("saveAs");
                }}
                testId="polish-look-saveas"
            />
            {p.modified ? <Btn icon="undo" iconSize={12} title={t("Placemaker.UI.Polish.Revert2", "Back to saved")} onClick={() => act.polishLook("revert")} testId="polish-look-revert" /> : null}
            {!p.modified && look.edited ? (
                <Btn icon="reset" iconSize={12} title={t("Placemaker.UI.Polish.ResetBuiltIn", "Back to {0} as it ships", [display])} onClick={() => apply("reset")} testId="polish-look-reset" />
            ) : null}
            {look.custom ? (
                <Btn
                    icon="edit"
                    iconSize={12}
                    title={t("Placemaker.UI.Polish.Rename", "Rename")}
                    onClick={() => {
                        setName(look.name);
                        setNaming("rename");
                    }}
                    testId="polish-look-rename"
                />
            ) : null}
            {look.custom ? <Btn icon="trash" iconSize={12} title={t("Placemaker.UI.Polish.Delete2", "Delete this outgrow type")} onClick={() => setAsking(true)} testId="polish-look-delete" /> : null}
            {applying ? (
                <Confirm
                    title={
                        applying === "save"
                            ? t("Placemaker.UI.Polish.SaveTitle", "Save “{0}”?", [display])
                            : t("Placemaker.UI.Polish.ResetTitle", "Put “{0}” back as it ships?", [display])
                    }
                    message={
                        (applying === "save"
                            ? t("Placemaker.UI.Polish.SaveMessage", "{0} building(s) use it. They change to the new settings right away, a few at a time.", [num(using)])
                            : t("Placemaker.UI.Polish.ResetMessage", "{0} building(s) use it. They change back right away, a few at a time.", [num(using)])) +
                        (using > 20 ? " " + t("Placemaker.UI.Polish.ApplyMany", "To keep the old settings too, cancel and use Copy as new type first.") : "")
                    }
                    confirm={applying === "save" ? t("Placemaker.UI.Polish.SaveOk", "Save") : t("Placemaker.UI.Polish.ResetOk", "Put back")}
                    onConfirm={() => {
                        act.polishLook(applying);
                        setApplying(null);
                    }}
                    onCancel={() => setApplying(null)}
                />
            ) : null}
            {asking ? (
                <Confirm
                    title={t("Placemaker.UI.Polish.DeleteTitle2", "Delete this outgrow type?")}
                    message={t("Placemaker.UI.Polish.DeleteMessage2", "Buildings outgrown with “{0}” keep how they look until they are outgrown again; zones that used it go back to their own.", [look.name])}
                    confirm={t("Placemaker.UI.Polish.DeleteOk", "Delete")}
                    danger
                    onConfirm={() => {
                        act.polishLook("delete");
                        setAsking(false);
                    }}
                    onCancel={() => setAsking(false)}
                />
            ) : null}
        </div>
    );
};

const PLANTING: { id: string; en: string; tip: string }[] = [
    { id: "garden", en: "Garden", tip: "Lawns, planted beds, now and then a sand pit or a bench." },
    { id: "natural", en: "Natural", tip: "Painted wild ground, rock outcrops, log piles; no tidy beds." },
    { id: "tidy", en: "Tidy", tip: "Mowed lawns, hedges, flower beds; no rocks." },
    { id: "yard", en: "Yard", tip: "Worn ground, storage yards with pallets and rubble." },
];

/** The outgrow type picked (p.theme): its buttons, its colour, its planting, every setting as rows, grouped and folded. */
const LookEditor = ({ p }: { p: PainterState }) => {
    const { t } = useT();
    const look = p.looks?.find((x) => x.id === p.theme);
    return (
        <div className={d.options} data-testid="options">
            <LookBar p={p} />
            <Row id="color" label={t("Placemaker.UI.Polish.Color", "Colour")} tip={t("Placemaker.UI.Polish.ColorTip", "Its colour on the map (the lens's Types) and in the charts.")}>
                <div className={s.swatches} data-testid="polish-color">
                    {(p.swatches ?? []).map((hex) => (
                        <button
                            key={hex}
                            type="button"
                            className={classNames(s.swatchBtn, { [s.swatchBtnOn]: (look?.color ?? "").toLowerCase() === hex.toLowerCase() })}
                            style={{ backgroundColor: hex }}
                            data-testid={"polish-color-" + hex.substring(1)}
                            onClick={(e) => {
                                e.stopPropagation();
                                act.polishLook("color", hex);
                            }}
                        />
                    ))}
                </div>
            </Row>
            <Row
                id="theme"
                label={t("Placemaker.UI.Polish.Planting", "Planting style")}
                tip={t("Placemaker.UI.Polish.PlantingTip", "What is planted and placed in the land: Garden (lawns, beds, a sand pit or a bench), Natural (wild ground, rocks, logs), Tidy (lawns, hedges, flower beds), Yard (worn ground, pallets, rubble). The rows below fine-tune it.")}
                changed={p.lookTheme !== p.lookSavedTheme}
                onReset={() => act.polishKnob("theme", THEMES.indexOf(p.lookSavedTheme))}
            >
                <Segmented
                    testId="polish-base"
                    stretch
                    value={p.lookTheme}
                    onChange={(v) => act.polishKnob("theme", THEMES.indexOf(v))}
                    options={PLANTING.map((x) => ({ value: x.id, label: t("Placemaker.UI.Polish.Planting." + x.id, x.en), title: t("Placemaker.UI.Polish.Planting." + x.id, x.en), description: t("Placemaker.UI.Polish.PlantingTip." + x.id, x.tip) }))}
                />
            </Row>
            {GROUPS.map((g) => (
                <Collapsible
                    key={g.id}
                    id={"polish:" + g.id}
                    title={t("Placemaker.UI.Polish.Group." + g.id, g.en)}
                    icon={g.icon}
                    defaultOpen={!!g.open}
                    testId={"polish-group-" + g.id}
                    summary={changedIn(g.knobs, p) > 0 ? t("Placemaker.UI.Polish.GroupChanged", "{0} changed", [changedIn(g.knobs, p)]) : undefined}
                >
                    {g.knobs.map((k) => (
                        <KnobRow key={k.id} k={k} p={p} />
                    ))}
                </Collapsible>
            ))}
        </div>
    );
};

/** Every outgrow type as one row of chips, each in its colour ("By zone" first when given). A click picks it. */
const TypeChips = ({ p, zoneChip, testId = "polish-themes" }: { p: PainterState; zoneChip?: ReactNode; testId?: string }) => {
    const { t } = useT();
    const chip = (l: NonNullable<PainterState["looks"]>[number]) => (
        <Chip
            key={l.id}
            testId={"polish-theme-" + l.id}
            nav="preset"
            icon={l.custom ? "star" : LOOK_ICON[l.id] ?? THEME_ICON[l.theme] ?? "flower"}
            color={l.color || CUSTOM_COLOR}
            name={lookName(t, p, l.id)}
            on={p.theme === l.id}
            description={l.custom ? t("Placemaker.UI.Polish.YourType", "Yours, planted like {0}", [lookName(t, p, l.theme)]) : t("Placemaker.UI.Painter.ThemeTip." + l.id, "")}
            badge={(p.theme === l.id && p.modified) || l.edited ? <div className={b.dot} /> : null}
            onClick={() => act.polishTheme(l.id)}
        />
    );
    return (
        <div className={b.chips} data-testid={testId}>
            {zoneChip}
            {orderedLooks(p).map(chip)}
        </div>
    );
};

/** Step 2 of the Outgrow tool: what the area holds, the outgrow type to give it and every setting of that type. */
export const PolishDesign = ({ state }: { state: ToolState }) => {
    const { t } = useT();
    const p = state.painter;
    if (!p) return null;
    const color = categoryColor("neighbourhood");
    const hasArea = state.area.closed && state.area.areaM2 > 0;
    const byZone = p.theme === "zone";
    return (
        <div className={d.stage} data-testid="polish-design" data-theme={p.theme}>
            <div className={s.count} data-testid="painter-count" data-lots={p.lots} data-polished={p.polished}>
                <div className={s.swatch} />
                <div>
                    {hasArea
                        ? t("Placemaker.UI.Painter.Count", "{0} buildings in the area · {1} outgrown", [p.lots, p.polished])
                        : t("Placemaker.UI.Painter.Legend", "Green: outgrown buildings")}
                </div>
            </div>
            <div className={b.picker} data-testid="polish-style-picker" data-open="true">
                <div className={b.head}>
                    <div className={b.headToggle}>
                        <Icon name="chevronDown" size={11} tint="dim" />
                        <div className={b.headTitle}>{t("Placemaker.UI.Polish.PickType", "Outgrow type")}</div>
                    </div>
                    <div className={b.flex} />
                </div>
                <div className={b.browser}>
                    <TypeChips
                        p={p}
                        zoneChip={
                            <Chip
                                testId="polish-theme-zone"
                                nav="preset"
                                icon="zone"
                                color={color}
                                name={lookName(t, p, "zone")}
                                on={byZone}
                                description={t("Placemaker.UI.Painter.ThemeTip.zone3", "Each building gets its zone type's outgrow type (Outgrow, its Defaults)")}
                                onClick={() => act.polishTheme("zone")}
                            />
                        }
                    />
                </div>
            </div>
            {byZone ? (
                <div className={s.off} data-testid="polish-byzone-note">
                    <Icon name="info" size={12} tint="dim" />
                    <div className={s.offText}>{t("Placemaker.UI.Polish.ByZoneNote4", "Each building gets the outgrow type its zone type has. Change them in Outgrow (the switcher at the top), or pick any type here to outgrow the area with it.")}</div>
                </div>
            ) : (
                <LookEditor p={p} />
            )}
        </div>
    );
};

/** The outgrow types to pick from as a list (a dropdown's content): each with its colour; Off; back to its default. */
const LookChips = ({ p, value, testId, onPick, inherit, off = true, zoneDefault = false }: { p: PainterState; value: string; testId: string; onPick: (v: string) => void; inherit?: string; off?: boolean; zoneDefault?: boolean }) => {
    const { t } = useT();
    const options = [...orderedLooks(p).map((l) => l.id), ...(off ? ["off"] : [])];
    return (
        <div className={s.choiceList} data-testid={testId}>
            {zoneDefault ? (
                <button type="button" className={classNames(s.choiceItem, { [s.choiceItemOn]: value === "zone" })} data-testid={testId + "-zone"} onClick={(e) => { e.stopPropagation(); onPick("zone"); }}>
                    <Icon name="star" size={10} tint="warn" />
                    <div className={s.choiceText}>{t("Placemaker.UI.Polish.SetDefaultType", "Set to default type")}</div>
                </button>
            ) : null}
            {inherit ? (
                <button type="button" className={s.choiceItem} data-testid={testId + "-inherit"} onClick={(e) => { e.stopPropagation(); onPick(""); }}>
                    <Icon name="undo" size={10} tint="dim" />
                    <div className={s.choiceText}>{t("Placemaker.UI.Polish.BackToDefault", "Back to {0}", [lookName(t, p, inherit)])}</div>
                </button>
            ) : null}
            {options.map((o) => (
                <button
                    key={o}
                    type="button"
                    className={classNames(s.choiceItem, { [s.choiceItemOn]: o === value })}
                    data-testid={testId + "-" + o}
                    onClick={(e) => {
                        e.stopPropagation();
                        onPick(o);
                    }}
                >
                    {o === "off" ? <Icon name="eyeOff" size={10} tint="dim" /> : <div className={s.dot} style={{ backgroundColor: lookColor(p, o) }} />}
                    <div className={s.choiceText}>{lookName(t, p, o)}</div>
                </button>
            ))}
        </div>
    );
};

/** A look that has settings to edit (not Off). */
const editable = (p: PainterState, id: string) => !!id && id !== "off" && id !== KIND_AUTO && !!p.looks?.some((l) => l.id === id);

/** A dropdown button: the outgrow type's colour and name, a chevron. */
const TypeButton = ({ p, id, label, on, onClick, testId }: { p: PainterState; id: string; label?: string; on: boolean; onClick: () => void; testId: string }) => {
    const { t } = useT();
    return (
        <button
            type="button"
            className={classNames(s.typeBtn, { [s.typeBtnOn]: on })}
            data-testid={testId}
            onClick={(e) => {
                e.stopPropagation();
                onClick();
            }}
        >
            {id === "off" ? <Icon name="eyeOff" size={10} tint="dim" /> : id === "zone" ? <Icon name="star" size={10} tint="warn" /> : <div className={s.dot} style={{ backgroundColor: lookColor(p, id) }} />}
            <div className={s.choiceText}>{label ?? (id === "zone" ? t("Placemaker.UI.Polish.SetDefaultType", "Set to default type") : lookName(t, p, id))}</div>
            <Icon name="chevronDown" size={9} tint="dim" />
        </button>
    );
};

/**
 * A Zoning type's zone type, on its line: Outgrow on or off, which outgrow type its buildings get (the Zoning type's own
 * unless one is picked: the dropdown, its list under the line is ZoneOutgrowPick) and Edit (Global outgrow's editor on it).
 */
export const ZoneOutgrowInline = ({ p, value, typeLook, on, setOn, open, toggle, testId }: { p: PainterState; value: string; typeLook: string; on: boolean; setOn: (on: boolean) => void; open: boolean; toggle: () => void; testId: string }) => {
    const { t } = useT();
    const current = value || typeLook;
    return (
        <div className={classNames(s.zoBox, { [s.zoBoxOff]: !on })} data-testid={testId}>
            <Tip title={t("Placemaker.UI.ZoneMix.OutgrowOn", "Outgrow")} description={t("Placemaker.UI.ZoneMix.OutgrowOnTip", "On: this zone's buildings outgrow their lots with the outgrow type beside it. Off: they stay as the game makes them.")}>
                <div className={s.zoHead}>
                    <div className={s.zoLabel}>{t("Placemaker.UI.ZoneMix.CapOutgrow", "Outgrow")}</div>
                    <Switch testId={testId + "-on"} on={on} onChange={setOn} />
                </div>
            </Tip>
            <TypeButton p={p} id={current} on={open} onClick={() => on && toggle()} testId={testId + "-open"} />
            {editable(p, current) ? (
                <Tip title={t("Placemaker.UI.Polish.EditLook", "Edit {0}", [lookName(t, p, current)])} description={t("Placemaker.UI.ZoneMix.EditTypeTip2", "Opens this outgrow type in Outgrow's Edit tab: a change there is for every zone that uses it.")}>
                    <button
                        type="button"
                        className={s.zoEdit}
                        data-testid={testId + "-edit"}
                        onClick={(e) => {
                            e.stopPropagation();
                            openLookEditor(current);
                        }}
                    >
                        <Icon name="edit" size={11} tint="dim" />
                    </button>
                </Tip>
            ) : null}
        </div>
    );
};

/** The outgrow types to pick for a Zoning type's zone (under its line): back to the Zoning type's own, and old settings
 *  changed there before (cleared with a button). */
export const ZoneOutgrowPick = ({ p, value, typeLook, oldChanges, onClear, onPick, testId }: { p: PainterState; value: string; typeLook: string; oldChanges: number; onClear: () => void; onPick: (id: string) => void; testId: string }) => {
    const { t } = useT();
    return (
        <div className={s.pickBox} data-testid={testId + "-box"}>
            <LookChips p={p} value={value} off={false} inherit={value ? typeLook : undefined} testId={testId} onPick={onPick} />
            {oldChanges > 0 ? (
                <div className={s.pickFoot}>
                    <Btn icon="reset" iconSize={11} label={t("Placemaker.UI.ZoneMix.ClearOwn2", "Clear {0} settings changed here before", [oldChanges])} onClick={onClear} testId={testId + "-clear"} />
                </div>
            ) : null}
        </div>
    );
};

const KINDS: { id: "home" | "shop" | "office" | "industry"; city: "homes" | "shops" | "offices" | "industry"; en: string; icon: string }[] = [
    { id: "home", city: "homes", en: "Homes", icon: "people" },
    { id: "shop", city: "shops", en: "Shops", icon: "shop" },
    { id: "office", city: "offices", en: "Offices", icon: "building" },
    { id: "industry", city: "industry", en: "Industry", icon: "factory" },
];

/** A zone's region (the prefab name's EU / NA) and its name without it. */
function zoneLabel(t: TFunc, id: string): { region: string; name: string } {
    const m = /^(EU|NA)\s+(.*)$/.exec(id);
    const region = m ? m[1] : "";
    const local = t("Assets.NAME[" + id + "]", "");
    const name = local && local !== id ? local.replace(/^(EU|NA)\s+/, "") : humanize(m ? m[2] : id);
    return { region, name };
}

/** Zones of one name (EU and NA alike): one card. */
type ZoneRowData = { key: string; name: string; icon: string; regions: string[]; zones: PolishZone[] };

function zoneRows(t: TFunc, zones: PolishZone[]): ZoneRowData[] {
    const rows = new Map<string, ZoneRowData>();
    for (const z of zones) {
        const { region, name } = zoneLabel(t, z.id);
        const r = rows.get(name) ?? { key: z.id, name, icon: z.icon, regions: [], zones: [] };
        if (region && !r.regions.includes(region)) r.regions.push(region);
        if (!r.icon && z.icon) r.icon = z.icon;
        r.zones.push(z);
        rows.set(name, r);
    }
    // zones with the game's icon first (the zoning toolbar's), then the rest
    return [...rows.values()].sort((a, b) => (a.icon ? 0 : 1) - (b.icon ? 0 : 1));
}

const num = (n: number) => Math.round(n).toLocaleString("en-US");

type Ask = { what: "polishAll" | "unpolishAll" | "polishSome" | "unpolishSome"; scope?: string; label?: string } | null;

/** The Global outgrow window beside the panel: the whole city's buttons, new buildings, the brush, the lens, the numbers,
 *  then Defaults (Outgrow new buildings, each zone type's default outgrow type), Edit (every outgrow type and its
 *  settings) and Numbers. */
/** Its children drawn again only when <fp> changes: the panel's state comes twice a second, the long lists need not be
 *  laid out again each time (Gameface: a quarter-second hitch every half second with the window open). */
const Frozen = React.memo(({ children }: { fp: string; children: ReactNode }) => <>{children}</>, (a, b) => a.fp === b.fp);

/** The Outgrow tab last shown: Outgrow opens on it again (the panel closed and opened, the mode switched). */
let lastCityTab: "zones" | "edit" | "numbers" = "zones";

export const PolishCityCard = ({ state }: { state: ToolState }) => {
    const { t } = useT();
    const [ask, setAsk] = useState<Ask>(null);
    const [picking, setPicking] = useState<string | null>(null);   // a zone id, or "brush"
    const [tab, setTabState] = useState<"zones" | "edit" | "numbers">(() => (pendingLook != null ? "edit" : lastCityTab));
    const setTab = (v: "zones" | "edit" | "numbers") => {
        lastCityTab = v;
        setTabState(v);
    };
    const [brushLook, setBrushLook] = useState<string>("");
    // an outgrow type to edit asked for elsewhere (a zone's dropdown in Zoning): the Edit tab, that type picked
    const takeRequest = useCallback(() => {
        if (pendingLook == null) return;
        const id = pendingLook;
        pendingLook = null;
        setTab("edit");
        if (id && id !== "off" && id !== KIND_AUTO) act.polishTheme(id);
    }, []);
    useEffect(() => takeRequest(), [takeRequest]);
    useLookEditorRequests(takeRequest);
    // Outgrow shown: the lens shows (and the brush stops when it goes)
    useEffect(() => {
        act.polishCity("cityOpen", "true");
        return () => act.polishCity("cityOpen", "false");
    }, []);
    const p = state.painter;
    if (!p || !p.city) return null;
    const city = p.city;
    const st = p.stats;
    const L = (id: string, en: string, args?: (string | number)[]) => t("Placemaker.UI.Polish." + id, en, args);
    const zones: PolishZone[] = p.zones ?? [];
    const brushType = brushLook || p.brushLook || (editable(p, p.theme) ? p.theme : "houses");
    const editLook = (id: string) => {
        setTab("edit");
        setPicking(null);
        act.polishTheme(id);
    };
    const kindName = (k: (typeof KINDS)[number]) => t("Placemaker.UI.Polish.Kind." + k.city, k.en);
    // what the long tabs show (they are drawn again only when it changes)
    const looksFp = JSON.stringify((p.looks ?? []).map((l) => [l.id, l.name, l.color, l.edited, l.theme]));
    const zonesFp = tab === "zones" ? JSON.stringify([p.zones, looksFp, st?.zones, st?.kindTotal, st?.kindPolished, picking, p.newHouses, p.levelUps, p.gameChanges, p.gameRemovals]) : "";
    const editFp = tab === "edit" ? JSON.stringify([p.theme, p.looks, p.values, p.lookValues, p.lookTheme, p.lookSavedTheme, p.modified, p.swatches, st?.looks?.[p.theme ?? ""]]) : "";
    return (
        <div className={s.cityEmbedded} data-testid="polish-city" onMouseDown={(e) => e.stopPropagation()}>
                {/* this city's master switch (saved with it): off pauses Outgrow, nothing is put back */}
                <div className={classNames(s.masterRow, { [s.masterRowOff]: !p.featureOn })} data-testid="polish-master">
                    <Tip title={L("Master", "Outgrow")} description={L("Master.Tip", "This city's master switch. Off: nothing outgrows (new buildings, zoned areas, what you outgrow), and nothing is put back: outgrown buildings stay as they are. Saved with the city.")}>
                        <div className={s.masterLabel}>{p.featureOn ? L("MasterOn", "Outgrow is on in this city") : L("MasterOff", "Outgrow is paused in this city")}</div>
                    </Tip>
                    <Switch testId="polish-on" on={p.featureOn} onChange={(v) => act.polishCity("on", v ? "true" : "false")} />
                </div>
                <div className={s.cityActions}>
                    <Btn variant="accent" icon="sparkle" iconSize={13} label={L("OutgrowAllShort2", "Set all to defaults")} title={L("OutgrowAll2", "Outgrow every building in the city with its zone type's default outgrow type (the Defaults tab), also the ones you brushed")} testId="polish-all" onClick={() => setAsk({ what: "polishAll" })} />
                    <Btn variant="ghost" icon="reset" iconSize={13} label={L("RemoveAllShort2", "Remove all")} title={L("RemoveAll", "Put every outgrown building in the city back as the game made it")} testId="polish-none" disabled={city.polished === 0} onClick={() => setAsk({ what: "unpolishAll" })} />
                </div>
                <StatusStrip polished={city.polished} total={city.total} st={st} />
                <div className={s.tools}>
                    <div className={s.toolCol} data-testid="polish-brush">
                        <div className={classNames(s.toolHead, s.brushHead)}>
                            <TypeButton p={p} id={brushType} on={picking === "brush"} onClick={() => setPicking(picking === "brush" ? null : "brush")} testId="polish-brush-type" />
                        </div>
                        <div className={s.toolRow}>
                            <Btn icon="brush" iconSize={11} label={L("BrushPaint", "Paint")} selected={p.brush === "paint"} title={brushType === "zone" ? L("BrushDefaultTip", "Drag over buildings: every one the brush touches lights up; let go and each gets its own zone type's default outgrow type again, first in the queue. Right-click drops the stroke.") : L("BrushPaintTip3", "Drag over buildings: every one the brush touches lights up; let go and they all get this outgrow type, first in the queue. Right-click drops the stroke.")} onClick={() => act.polishCity("brush", p.brush === "paint" ? "off" : "paint:" + brushType)} testId="polish-brush-paint" />
                            <Btn icon="trash" iconSize={11} label={L("BrushErase", "Erase")} selected={p.brush === "erase"} title={L("BrushEraseTip2", "Drag over buildings: every one the brush touches lights up; let go and their outgrow is removed")} onClick={() => act.polishCity("brush", p.brush === "erase" ? "off" : "erase")} testId="polish-brush-erase" />
                        </div>
                        {p.brush ? (
                            <div className={s.brushSize} data-testid="polish-brush-size">
                                <Slider testId="polish-brush-size-slider" value={Math.round(p.brushSize ?? 40)} min={8} max={200} step={2} unit=" m" onPreview={() => {}} onCommit={(v) => act.polishCity("brushSize", String(v))} />
                            </div>
                        ) : null}
                    </div>
                    <div className={s.toolCol} data-testid="polish-lens">
                        <div className={s.toolHead}>
                            <Icon name="eye" size={12} tint="light" />
                            <div className={s.toolTitle}>{L("Lens", "Map lens")}</div>
                            <div className={s.flex} />
                            <Switch testId="polish-lens-on" on={!!p.lensWanted} onChange={(v) => act.polishCity("lens", v ? "true" : "false")} />
                        </div>
                        <div className={s.toolRow}>
                            <Segmented
                                testId="polish-lens-mode"
                                stretch
                                value={p.lensMode === "types" ? "types" : "status"}
                                onChange={(v) => act.polishCity("lensMode", v)}
                                options={[
                                    { value: "status", label: L("LensStatus", "Status"), description: L("LensStatusTip", "Outgrown, not, being fitted, and the queue by how long each has waited") },
                                    { value: "types", label: L("LensTypes", "Types"), description: L("LensTypesTip", "Every building in its outgrow type's colour") },
                                ]}
                            />
                        </div>
                    </div>
                </div>
                {picking === "brush" ? (
                    <div className={s.topPick}>
                        <LookChips
                            p={p}
                            value={brushType}
                            off={false}
                            zoneDefault
                            testId="polish-brush-pick"
                            onPick={(v) => {
                                setBrushLook(v);
                                if (p.brush === "paint") act.polishCity("brush", "paint:" + v);
                                setPicking(null);
                            }}
                        />
                    </div>
                ) : null}
                {p.lensWanted ? (
                    <div className={s.legendStrip}>
                        {p.lensMode === "types" ? (
                            <div className={s.legendGrid} data-testid="polish-lens-types">
                                {(p.lensTypes ?? []).map((x) => (
                                    <div key={x.id} className={s.legendCell}>
                                        <div className={s.dot} style={{ backgroundColor: x.id === "-" ? NOT_OUTGROWN : lookColor(p, x.id) }} />
                                        <div className={s.legendCellName}>{x.id === "-" ? L("NotOutgrown", "Not outgrown") : x.id === "" ? L("NoType", "No type") : lookName(t, p, x.id)}</div>
                                        <div className={s.legendCellCount}>{num(x.count)}</div>
                                    </div>
                                ))}
                            </div>
                        ) : (
                            <LensLegend p={p} />
                        )}
                    </div>
                ) : null}
                <ScrollArea className={pn.bodyWrap} testId="polish-city-body">
                    <div className={classNames(pn.body, s.cityBody)}>
                        <div className={s.cityTabs}>
                            <Segmented
                                testId="polish-city-tab"
                                stretch
                                value={tab}
                                onChange={(v) => setTab(v as "zones" | "edit" | "numbers")}
                                options={[
                                    { value: "zones", icon: "zone", label: L("TabDefaults", "Defaults"), description: L("TabDefaultsTip", "Each zone type's default outgrow type: what new buildings and Set all to defaults give its buildings") },
                                    { value: "edit", icon: "edit", label: L("TabEdit", "Edit"), description: L("TabEditTip", "Every outgrow type and its settings: change one, or make your own") },
                                    { value: "numbers", icon: "layers", label: L("TabNumbers", "Numbers"), description: L("TabNumbersTip", "Outgrown by type and by kind, the queue, speed, frames per second") },
                                ]}
                            />
                        </div>
                        {tab === "numbers" ? (
                            st ? (
                                <OutgrowStats p={p} st={st} typeName={(id) => lookName(t, p, id)} typeColor={(id) => lookColor(p, id)} />
                            ) : (
                                <div className={s.off}>
                                    <Icon name="info" size={12} tint="dim" />
                                    <div className={s.offText}>{L("NoCity", "The numbers come once the city has run for a moment.")}</div>
                                </div>
                            )
                        ) : tab === "zones" ? (
                            <Frozen fp={zonesFp}>
                            <div data-testid="polish-city-zones">
                                <div className={classNames(s.newCard, { [s.newCardOn]: p.newHouses })} data-testid="polish-new-card">
                                    <Tip title={L("AutoNew", "Auto outgrow new buildings")} description={L("AutoNew.Tip", "Buildings that grow get outgrown. Kept with this city.")}>
                                        <div className={s.newText}>
                                            <div className={s.newLabel}>{L("AutoNew", "Auto outgrow new buildings")}</div>
                                            <div className={s.newSub}>{L("AutoNew.Sub", "Each with its zone type, below")}</div>
                                        </div>
                                    </Tip>
                                    <Switch testId="polish-newHouses" on={p.newHouses} onChange={(v) => act.polishCity("newHouses", v ? "true" : "false")} />
                                </div>
                                <div className={classNames(s.newCard, { [s.newCardOn]: p.levelUps })} data-testid="polish-levelups-card">
                                    <Tip title={L("AutoLevelUps", "Auto outgrow when a building levels up")} description={L("AutoLevelUps.Tip", "Off: an upgraded building keeps its garden as it is.")}>
                                        <div className={s.newText}>
                                            <div className={s.newLabel}>{L("AutoLevelUps", "Auto outgrow when a building levels up")}</div>
                                            <div className={s.newSub}>{L("AutoLevelUps.Sub", "Off: it keeps its garden")}</div>
                                        </div>
                                    </Tip>
                                    <Switch testId="polish-levelUps" on={p.levelUps} onChange={(v) => act.polishCity("levelUps", v ? "true" : "false")} />
                                </div>
                                <div className={classNames(s.newCard, { [s.newCardOn]: p.gameChanges })} data-testid="polish-gamechanges-card">
                                    <Tip title={L("AutoChanges", "Auto outgrow when a building changes")} description={L("AutoChanges.Tip", "Off: its garden stays as it is.")}>
                                        <div className={s.newText}>
                                            <div className={s.newLabel}>{L("AutoChanges", "Auto outgrow when a building changes")}</div>
                                            <div className={s.newSub}>{L("AutoChanges.Sub", "Abandoned, burned, condemned")}</div>
                                        </div>
                                    </Tip>
                                    <Switch testId="polish-gameChanges" on={p.gameChanges} onChange={(v) => act.polishCity("gameChanges", v ? "true" : "false")} />
                                </div>
                                <div className={classNames(s.newCard, { [s.newCardOn]: p.gameRemovals })} data-testid="polish-gameremovals-card">
                                    <Tip title={L("AutoRemovals", "Auto outgrow when a building nearby is removed")} description={L("AutoRemovals.Tip", "When the game clears a building, the gardens around take its land. Off: they stay as they are. Your own bulldozing always counts.")}>
                                        <div className={s.newText}>
                                            <div className={s.newLabel}>{L("AutoRemovals", "Auto outgrow when a building nearby is removed")}</div>
                                            <div className={s.newSub}>{L("AutoRemovals.Sub", "Gardens grow into the empty lot")}</div>
                                        </div>
                                    </Tip>
                                    <Switch testId="polish-gameRemovals" on={p.gameRemovals} onChange={(v) => act.polishCity("gameRemovals", v ? "true" : "false")} />
                                </div>
                                <div className={s.zhead}>{L("ZonesHead2", "Each zone type's default outgrow type, and how many of its buildings are outgrown (open a group)")}</div>
                                {KINDS.map((k, ki) => {
                                    const list = zones.filter((z) => z.kind === k.id);
                                    if (!list.length) return null;
                                    const kt = st?.kindTotal[ki] ?? 0, kp = st?.kindPolished[ki] ?? 0;
                                    return (
                                        <Collapsible
                                            key={k.id}
                                            id={"polish:zones3:" + k.id}
                                            title={kindName(k)}
                                            icon={k.icon}
                                            defaultOpen={false}
                                            testId={"polish-zones-" + k.id}
                                            summary={st ? `${num(kp)} / ${num(kt)}` : undefined}
                                            right={
                                                <div className={s.kindBtns} onClick={(e) => e.stopPropagation()} onMouseDown={(e) => e.stopPropagation()}>
                                                    <Btn icon="sparkle" iconSize={11} title={L("OutgrowKind2", "Set all {0} to their defaults", [kindName(k).toLowerCase()])} testId={"polish-kind-" + k.id + "-all"} onClick={() => setAsk({ what: "polishSome", scope: "kind:" + k.id, label: kindName(k).toLowerCase() })} />
                                                    <Btn icon="reset" iconSize={11} title={L("RemoveKind", "Remove outgrow from {0}", [kindName(k).toLowerCase()])} disabled={kp === 0} testId={"polish-kind-" + k.id + "-none"} onClick={() => setAsk({ what: "unpolishSome", scope: "kind:" + k.id, label: kindName(k).toLowerCase() })} />
                                                </div>
                                            }
                                        >
                                            {zoneRows(t, list).map((r) => {
                                                const on = picking === r.key;
                                                const looks = [...new Set(r.zones.map((z) => z.look))];
                                                const look = looks.length === 1 ? looks[0] : "";
                                                const own = r.zones.some((z) => z.own);
                                                const counts = r.zones.reduce((a, z) => { const cz = st?.zones[z.id]; return cz ? [a[0] + cz[0], a[1] + cz[1]] : a; }, [0, 0]);
                                                return (
                                                    <React.Fragment key={r.key}>
                                                        <div className={classNames(s.zrow, { [s.zrowOn]: on, [s.zrowEmpty]: counts[0] === 0 })} data-testid={"polish-zone-" + r.key} data-look={look}>
                                                            <Icon name={r.icon || "zone"} size={12} fallback="zone" />
                                                            <div className={s.zrowName}>{r.name.replace(/\s+Housing$/i, "")}</div>
                                                            <div className={s.zrowCount}>{`${num(counts[1])} / ${num(counts[0])}`}</div>
                                                            <TypeButton p={p} id={look || "off"} label={look ? undefined : L("Mixed", "Mixed")} on={on} onClick={() => setPicking(on ? null : r.key)} testId={"polish-zone-" + r.key + "-type"} />
                                                            {editable(p, look) ? <Btn icon="edit" iconSize={10} title={L("EditLook", "Edit {0}", [lookName(t, p, look)])} onClick={() => editLook(look)} testId={"polish-zone-" + r.key + "-edit"} /> : <div className={s.zrowEditGap} />}
                                                        </div>
                                                        {on ? (
                                                            <div className={s.zonePick}>
                                                                <LookChips
                                                                    p={p}
                                                                    value={look}
                                                                    inherit={own ? r.zones[0].def || undefined : undefined}
                                                                    testId={"polish-zone-" + r.key + "-pick"}
                                                                    onPick={(v) => {
                                                                        for (const z of r.zones) act.polishZone(z.id, v);
                                                                        setPicking(null);
                                                                    }}
                                                                />
                                                                <div className={s.pickFoot}>
                                                                    <Btn icon="sparkle" iconSize={11} label={L("OutgrowZone2", "Set these to it")} testId={"polish-zone-" + r.key + "-all"} onClick={() => setAsk({ what: "polishSome", scope: "zone:" + r.key, label: r.name })} />
                                                                    <Btn variant="ghost" icon="reset" iconSize={11} label={L("RemoveZone", "Remove from these")} disabled={counts[1] === 0} testId={"polish-zone-" + r.key + "-none"} onClick={() => setAsk({ what: "unpolishSome", scope: "zone:" + r.key, label: r.name })} />
                                                                </div>
                                                            </div>
                                                        ) : null}
                                                    </React.Fragment>
                                                );
                                            })}
                                        </Collapsible>
                                    );
                                })}
                            </div>
                            </Frozen>
                        ) : (
                            <Frozen fp={editFp}>
                            <div data-testid="polish-city-edit">
                                <div className={b.picker} data-open="true">
                                    <div className={b.browser}>
                                        <TypeChips p={p} />
                                    </div>
                                </div>
                                {editable(p, p.theme) ? (
                                    <LookEditor p={p} />
                                ) : (
                                    <div className={s.off}>
                                        <Icon name="info" size={12} tint="dim" />
                                        <div className={s.offText}>{L("PickToEdit2", "Pick an outgrow type to see and change it. Saving a built-in one changes it everywhere it is used; Copy as new makes your own.")}</div>
                                    </div>
                                )}
                            </div>
                            </Frozen>
                        )}
                    </div>
                </ScrollArea>
            {ask ? (
                <Confirm
                    title={
                        ask.what === "polishAll"
                            ? L("OutgrowAll.Title2", "Set the whole city to its defaults?")
                            : ask.what === "unpolishAll"
                              ? L("RemoveAll.Title", "Remove all outgrow?")
                              : ask.what === "polishSome"
                                ? L("OutgrowSome.Title2", "Set all {0} to their defaults?", [ask.label ?? ""])
                                : L("RemoveSome.Title", "Remove outgrow from {0}?", [ask.label ?? ""])
                    }
                    message={
                        ask.what === "polishAll" || ask.what === "polishSome"
                            ? t("Placemaker.UI.Polish.PolishAll.ByZone3", "Every building is outgrown with its zone type's default outgrow type, also the ones you brushed another type onto (zone types set to Off are left out). It runs in the background a few buildings at a time, so the game keeps running smoothly.")
                            : t("Placemaker.UI.Polish.UnpolishAll.Message2", "The outgrown buildings go back to how the game made them: fences, hedges and lawns back, the beds, paths, bushes and paving taken away (ground paint stays). It runs in the background a few hundred at a time.")
                    }
                    confirm={ask.what === "polishAll" || ask.what === "polishSome" ? L("OutgrowAll.Ok2", "Set to defaults") : L("RemoveAll.Ok", "Remove all")}
                    danger={ask.what === "unpolishAll" || ask.what === "unpolishSome"}
                    onConfirm={() => {
                        act.polishCity(ask.what, ask.scope ?? "");
                        setAsk(null);
                    }}
                    onCancel={() => setAsk(null)}
                />
            ) : null}
        </div>
    );
};
