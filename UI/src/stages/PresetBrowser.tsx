// s42 — the Place library: pick a place, which is also what you are editing (docs/proposals/s42.md; the Editing bar merged
// in, the user, 25 Oct: "they essentially do the same thing"; "you're like browsing your place library").
//   The section header (SectionHead: PLACEMAKER LIBRARY · pick a place · shuffle · search), then one bordered panel,
//     three numbered rows of equal-height chips: 1 Category · 2 Type · 3 Style; the lit chip of each row is what is being
//     edited. Always open (user: no collapse button for it).
//     Each chip = coloured icon tile (category colour; types and styles inherit it) + name + small count. Styles start with
//     Standard (the type's own design, a style like the others: the old "Default"), end with the player's own (gold star).
//     The search icon in the header filters all three rows.
//   The save strip, the panel's last line: where the style stands (a hint, "Your version of …" or ● Unsaved changes) and,
//     with changes, Undo · Save (the player's own) or Save over (a built-in, reset to shipped any time) · then always Save as
//     new · ⋯ (rename, delete, reset to shipped, export). Naming and the ⋯ actions take the strip's line.
// Styles are the engine's presets (selectPreset / savePreset / savePresetAs …); categories come from the central shelf
// (Recipes/shelf/<category>.json, Core/Tool/Shelf.cs): the game sends each type's category (RecipeInfo.category), what the
// shelf hides (RecipeInfo.shelved, PresetView.shelved) and the categories in order (Catalog.categories).
import React, { KeyboardEvent, useEffect, useRef, useState } from "react";
import classNames from "classnames";
import { ActivePreset, Catalog, DesignState, LayerType, PresetView, RecipeInfo, StepView } from "../contract";
import { act } from "../lib/bindings";
import { humanize, TFunc, useT } from "../lib/l10n";
import { Tip } from "../lib/tips";
import { useOpen } from "../lib/collapse";
import { categoryColor, categoryIcon, CUSTOM_COLOR, styleIcon, typeIcon } from "../lib/palette";
import { Btn, Icon } from "../components/common";
import { Confirm } from "../components/dialog";
import { SectionHead } from "./SectionHead";
import s from "./browser.module.scss";

// ───────────── types & groups ─────────────

/**
 * A recipe is a "mix" when its id names more than one layer ("roads-with-parks", "split-park-plaza",
 * "park-with-playground"); a single layer or a variant of one ("parking", "parking-green") is a type.
 */
export function isMix(catalog: Catalog, r: RecipeInfo) {
    if (r.custom) return true;
    const ids = new Set(catalog.layers.map((l) => l.id.toLowerCase()));
    const named = r.id
        .toLowerCase()
        .split(/[-_+|]+/)
        .filter((tok) => ids.has(tok) || (tok.endsWith("s") && ids.has(tok.slice(0, -1))));
    return named.length > 1;
}

/**
 * Types: recipes that ARE a single fill type (id equals a visible fill layer's id). Mixes and player
 * recipes are shelved in the UI. Falls back to the id heuristic when no recipe matches a layer id.
 */
export function makeRecipes(catalog: Catalog): RecipeInfo[] {
    const fills = new Set(catalog.layers.filter((l) => l.kind === "fill" && !l.hidden).map((l) => l.id.toLowerCase()));
    const singles = catalog.recipes.filter((r) => !r.custom && !r.painter && fills.has(r.id.toLowerCase()));
    const types = singles.length ? singles : catalog.recipes.filter((r) => !r.painter && !isMix(catalog, r));
    // (PROTOCOL 25) the neighbourhood-polish painter is picked like a type (it has no layer of its own)
    const painter = catalog.recipes.filter((r) => r.painter);
    // (core) The central shelf hides whole categories (industry, transport) and single types (RecipeInfo.shelved).
    return [...types, ...painter].filter((r) => !r.shelved);
}

export type MakeGroup = { id: string; key: string; en: string };
const OTHER_GROUP: MakeGroup = { id: "other", key: "Placemaker.UI.MakeGroup.Other", en: "Other" };

/**
 * The categories of the Make row, in the shelf's order (Recipes/shelf/<category>.json: "fewer, better", user 2026-10-15),
 * hidden ones left out, "Other" last. Without shelf data every type group is its own category, in first-seen order.
 */
export function makeGroups(catalog: Catalog): MakeGroup[] {
    const shelf = (catalog.categories ?? []).filter((c) => !c.hidden).slice().sort((a, b) => a.order - b.order);
    if (shelf.length) return [...shelf.map((c) => ({ id: c.id, key: c.name, en: c.en })), OTHER_GROUP];
    const seen: string[] = [];
    for (const r of catalog.recipes) {
        const g = recipeGroup(r);
        if (g !== "other" && !seen.includes(g)) seen.push(g);
    }
    return [...seen.map((g) => ({ id: g, key: "Placemaker.UI.MakeGroup." + g.charAt(0).toUpperCase() + g.slice(1), en: humanize(g) })), OTHER_GROUP];
}

/** Category of a recipe: the shelf's (RecipeInfo.category), else the group its type declares, else guessed from the id. */
export function recipeGroup(r: RecipeInfo): string {
    return r.category || r.group || makeGroupOf(r.id);
}

/** Fallback group of a type id that declares none ("parking" before "park", "market square" before parks). */
export function makeGroupOf(id: string): string {
    const x = id.toLowerCase();
    if (/mall|retail|commercial|shopping/.test(x)) return "commercial";
    if (/parking|car-?park|garage|lot/.test(x)) return "parking";
    if (/road|street|avenue|grid/.test(x)) return "roads";
    if (/plaza|square|market|piazza|amphi|festival|fair/.test(x)) return "squares";
    if (/park|garden|camp|reserve|nature|forest|picnic|arboret|cemetery|green|playground|sport|golf|beach|lido|promenade|allotment|meadow|zoo|dog|botanic|skate/.test(x)) return "parks";
    return "other";
}

function typeLayer(catalog: Catalog, r: RecipeInfo): LayerType | undefined {
    const id = r.id.toLowerCase();
    return catalog.layers.find((l) => l.id.toLowerCase() === id);
}

/**
 * (C# PresetStore.StandardId) The style every type has, first in its list and picked with the type: the type's own design.
 * It used to be the unnamed "Default" chip (no preset); now it is a style like the others (user 2026-10-20).
 */
export const STANDARD_ID = "standard";

export const presetName = (t: TFunc, p: PresetView) =>
    p.custom ? p.name : p.id === STANDARD_ID && !p.name ? t("Placemaker.UI.Preset.Standard", "Standard") : t(p.name, humanize(p.name || p.id));
const recipeName = (t: TFunc, r: RecipeInfo) => (r.custom ? r.name : t(r.name, humanize(r.name || r.id)));

/** A style's glyph: the player's own a star, Standard the type's own glyph, the others by keyword (lib/palette styleIcon). */
const presetIcon = (p: PresetView, name: string, typeGlyph: string) =>
    p.custom ? "star" : p.id === STANDARD_ID ? typeGlyph : styleIcon(p.id, name, typeGlyph);

/** The styles the Style row lists for a type: built-ins the shelf shows (Standard first, as the game sends them), then the player's. */
const shownStyles = (layer: LayerType, activeId: string | null) => [
    ...layer.presets.filter((p) => !p.custom && (!p.shelved || p.id === activeId)),
    ...layer.presets.filter((p) => p.custom),
];

/**
 * The style shown as picked: the game's active one; a design that names none (an older game version) is on Standard.
 * Null when the active style belongs to another type.
 */
function shownActive(layer: LayerType | undefined, ap: ActivePreset | undefined): string | null {
    if (!layer || !ap || ap.layer.toLowerCase() !== layer.id.toLowerCase()) return null;
    return ap.id || (layer.presets.some((p) => p.id === STANDARD_ID) ? STANDARD_ID : null);
}

/** "Park · Formal garden". */
export function makeSummary(t: TFunc, catalog: Catalog, design: DesignState): string {
    const r = catalog.recipes.find((x) => x.id === design.recipeId);
    if (!r) return "";
    const layer = typeLayer(catalog, r);
    const id = shownActive(layer, design.activePreset);
    const p = id && layer ? layer.presets.find((x) => x.id === id) : undefined;
    return p ? recipeName(t, r) + " · " + presetName(t, p) : recipeName(t, r);
}

// ───────────── remembered choices ─────────────

const CATEGORY_KEY = "placemaker.browserCategory.v2";
const USE_KEY = "placemaker.categoryUse.v1";

function load<T>(key: string, fallback: T): T {
    try {
        const raw = window.localStorage?.getItem(key);
        return raw ? (JSON.parse(raw) as T) : fallback;
    } catch {
        return fallback;
    }
}

function save(key: string, v: unknown) {
    try {
        window.localStorage?.setItem(key, JSON.stringify(v));
    } catch {
        /* not remembered */
    }
}

/** Counts a type pick for its category ("most used" categories come first). */
function countUse(group: string) {
    const use = load<Record<string, number>>(USE_KEY, {});
    use[group] = (use[group] ?? 0) + 1;
    save(USE_KEY, use);
}

/** Categories shown before "N more". */
const SHOWN_CATEGORIES = 7;

// ───────────── chips ─────────────

/** Coloured icon tile with a dark glyph (category colour; types and styles inherit it). */
export const Tile = ({ icon, color, size = 16 }: { icon: string; color: string; size?: number }) => (
    <div className={s.tile} style={{ backgroundColor: color, width: size + "rem", height: size + "rem" }}>
        <Icon name={icon} size={Math.round(size * 0.7)} tint="dark" />
    </div>
);

type ChipProps = {
    testId: string;
    nav: string;
    icon: string;
    color: string;
    name: string;
    count?: number;
    on?: boolean;
    title?: string;
    description?: string;
    dashed?: boolean;
    badge?: React.ReactNode;
    /** The name's own test id (the lit Style chip: the style being edited). */
    nameTestId?: string;
    extra?: Record<string, string>;
    onClick: () => void;
};

export const Chip = ({ testId, nav, icon, color, name, count, on, title, description, dashed, badge, nameTestId, extra, onClick }: ChipProps) => (
    <Tip title={title ?? name} description={description}>
        <button
            type="button"
            data-nav={nav}
            data-testid={testId}
            aria-pressed={on ? "true" : "false"}
            className={classNames(s.chip, { [s.chipOn]: on, [s.chipDashed]: dashed })}
            onClick={(e) => {
                e.stopPropagation();
                onClick();
            }}
            {...extra}
        >
            {dashed ? <Icon name={icon} size={12} tint="dim" /> : <Tile icon={icon} color={color} />}
            <div className={s.chipName} data-testid={nameTestId} data-ellipsis>
                {name}
            </div>
            {count !== undefined ? <div className={s.chipCount}>{count}</div> : null}
            {badge}
        </button>
    </Tip>
);

export const RowLabel = ({ n, label }: { n: number; label: string }) => (
    <div className={s.rowLabel}>
        <div className={s.rowNum}>{n}</div>
        <div className={s.rowLabelText}>{label}</div>
    </div>
);

// ───────────── picker ─────────────

export type BarMode = "idle" | "saveAs" | "rename" | "menu";

/**
 * The Place library: pick a place, and what you are editing, in one section (the user, 25 Oct: the picker and the Editing
 * bar "essentially do the same thing"): 1 Category · 2 Type · 3 Style, the lit chip of each row is what is being edited.
 * The save strip under the rows says where that style stands and saves it (over it, or as a new one). Search (header icon) filters the rows:
 * categories with a hit, the matching types of every category, the matching styles of every type. Arrow keys walk the
 * chips, Enter in the search box picks the first hit, Esc clears it.
 */
export const TypeBrowser = ({
    catalog, design, step, mode, setMode,
}: { catalog: Catalog; design: DesignState; step: StepView | null; mode: BarMode; setMode: (m: BarMode) => void }) => {
    const { t } = useT();
    const [query, setQuery] = useState("");
    const [searching, setSearching] = useState(false);
    const [moreCats, setMoreCats] = useOpen("design:moreCategories", false);
    const [confirm, setConfirm] = useState<"delete" | "reset" | null>(null);
    const [exported, setExported] = useState(false);
    const list = makeRecipes(catalog);
    // A shelved type picked on purpose (the game runner by id, editing an old build) still shows its styles while it is current.
    const selectedRecipe = list.find((r) => r.id === design.recipeId) ?? catalog.recipes.find((r) => r.id === design.recipeId && !r.custom && !!r.shelved);
    const selectedGroup = selectedRecipe ? recipeGroup(selectedRecipe) : "";
    const [chosen, setChosen] = useState<string | null>(() => load<string | null>(CATEGORY_KEY, null));
    const root = useRef<HTMLDivElement>(null);
    const q = query.trim().toLowerCase();
    const has = (text: string) => text.toLowerCase().includes(q);

    const cats = makeGroups(catalog).map((g, order) => ({ id: g.id, title: t(g.key, g.en), order, types: list.filter((r) => recipeGroup(r) === g.id) }))
        .filter((c) => c.types.length);   // the shelf's order (Recipes/shelf/*.json), "Other" last
    const exists = (id: string | null) => !!id && cats.some((c) => c.id === id);
    // No type picked (the tool cleared it, e.g. back to Design after "keep area"): no category looks picked either and the
    // Type row stays empty (user: "it looks like zoning is highlighted, but it's not ... it should just show nothing").
    // A category click picks its first type, so one click still starts a design.
    const category = !design.recipeId ? "" : exists(chosen) ? chosen! : exists(selectedGroup) ? selectedGroup : cats[0]?.id ?? "";
    const setCategory = (c: string) => {
        save(CATEGORY_KEY, c);
        setChosen(c);
    };
    // The selected type changed from elsewhere (undo, a search hit, a preset of another type): show its category.
    const lastRecipe = useRef(design.recipeId);
    useEffect(() => {
        if (lastRecipe.current === design.recipeId) return;
        lastRecipe.current = design.recipeId;
        if (selectedGroup && selectedGroup !== chosen) setCategory(selectedGroup);
        // eslint-disable-next-line react-hooks/exhaustive-deps
    }, [design.recipeId]);
    // A shelved last pick (e.g. Quarry, used before industry was hidden) is switched to the first type shown by the game when
    // it restores the last type (PlacemakerToolSystem.EnsureInitialized), so a hidden type picked on purpose stays.

    // search hits
    type Hit = { r: RecipeInfo; layer: LayerType; p: PresetView };
    const typeHits: RecipeInfo[] = [];
    const styleHits: Hit[] = [];
    const catHits = new Set<string>();
    if (q) {
        for (const c of cats) {
            const catHit = has(c.title);
            for (const r of c.types) {
                const layer = typeLayer(catalog, r);
                const ps = layer ? layer.presets.filter((p) => !p.shelved && has(presetName(t, p))) : [];
                if (layer) for (const p of ps) styleHits.push({ r, layer, p });
                if (catHit || has(recipeName(t, r)) || ps.length) {
                    typeHits.push(r);
                    catHits.add(c.id);
                }
            }
        }
    }

    // categories: the most used first; the current one and the selected type's always visible
    // All categories, always (user: not cluttered enough to need "N more").
    const visibleCats = q ? cats.filter((c) => catHits.has(c.id)) : cats;
    const hiddenCats = 0;
    const types = q ? typeHits : cats.find((c) => c.id === category)?.types ?? [];
    const firstHit = styleHits.length ? { recipe: styleHits[0].r.id, layer: styleHits[0].layer.id, preset: styleHits[0].p.id } : typeHits.length ? { recipe: typeHits[0].id, layer: typeHits[0].id, preset: null } : null;

    const ap = design.activePreset;
    const selLayer = selectedRecipe ? typeLayer(catalog, selectedRecipe) : undefined;
    const activeId = shownActive(selLayer, ap);
    const modified = !!ap?.modified;

    const pickType = (r: RecipeInfo) => {
        if (r.id === design.recipeId) return;
        countUse(recipeGroup(r));
        act.selectRecipe(r.id);
    };
    // Every style, Standard included, can be picked again at any time: a changed one goes back to it as saved.
    const pickStyle = (r: RecipeInfo, layer: LayerType, p: PresetView) => {
        const active = r.id === design.recipeId && !!ap && ap.id === p.id;
        if (active && !modified) return;
        if (r.id !== design.recipeId) countUse(recipeGroup(r));
        act.selectPreset(layer.id, p.id);
    };

    const chips = () => Array.from(root.current?.querySelectorAll<HTMLElement>("[data-nav]") ?? []);
    /** Arrow keys walk the chips: ←/→ previous / next, ↑/↓ the nearest chip in the row above / below. */
    const onKey = (e: KeyboardEvent<HTMLDivElement>) => {
        const all = chips();
        const i = all.indexOf(document.activeElement as HTMLElement);
        let next: HTMLElement | undefined;
        if (e.key === "ArrowRight") next = all[i < 0 ? 0 : Math.min(all.length - 1, i + 1)];
        else if (e.key === "ArrowLeft") next = i <= 0 ? undefined : all[i - 1];
        else if (e.key === "ArrowDown" || e.key === "ArrowUp") {
            const down = e.key === "ArrowDown";
            if (i < 0) next = all[0];
            else {
                const r = all[i].getBoundingClientRect();
                const cx = r.left + r.width / 2,
                    cy = r.top + r.height / 2;
                let best: { el: HTMLElement; dy: number; dx: number } | null = null;
                for (const el of all) {
                    const b = el.getBoundingClientRect();
                    const dy = (b.top + b.height / 2 - cy) * (down ? 1 : -1);
                    if (dy < 4) continue;
                    const dx = Math.abs(b.left + b.width / 2 - cx);
                    if (!best || dy < best.dy - 4 || (Math.abs(dy - best.dy) <= 4 && dx < best.dx)) best = { el, dy, dx };
                }
                next = best?.el ?? (down ? undefined : root.current?.querySelector<HTMLElement>("input") ?? undefined);
            }
        } else if (e.key === "Home") next = all[0];
        else if (e.key === "End") next = all[all.length - 1];
        else if (e.key === "Enter") {
            // Enter activates the focused chip (native click); keep it from reaching the tool's Enter = primary.
            e.stopPropagation();
            return;
        } else return;
        e.preventDefault();
        e.stopPropagation();
        next?.focus();
    };

    const closeSearch = () => {
        setQuery("");
        setSearching(false);
    };

    // The style being edited (the design's root step): the lit Style chip. Standard is a style like the others: undo,
    // save over (reset to shipped), save as new all work on it.
    const editLayer = step ? catalog.layers.find((l) => l.id === step.layer) : undefined;
    const edit = step && ap && editLayer ? { step, ap, layer: editLayer } : null;
    const editId = edit ? shownActive(edit.layer, edit.ap) : null;
    const ep = edit && editId ? edit.layer.presets.find((x) => x.id === editId) ?? null : null;
    const editName = ep ? presetName(t, ep) : t("Placemaker.UI.Preset.Standard", "Standard");
    const changed = !!edit?.ap.modified;
    const canSave = changed && !!edit?.ap.canSave && !!ep;
    const close = () => setMode("idle");
    const revert = () => {
        if (edit && changed && ep) act.selectPreset(edit.layer.id, ep.id);
    };

    const head = (
        <SectionHead
            icon="layers"
            title={t("Placemaker.UI.Library.Title", "Placemaker library")}
            first
            testId="make-head"
            sub={searching ? undefined : t("Placemaker.UI.Library.Sub", "pick a place")}
            fill={
                searching ? (
                    <div className={s.search}>
                        <input
                            className={s.searchInput}
                            data-testid="browser-search"
                            autoFocus
                            value={query}
                            placeholder={t("Placemaker.UI.Browser.SearchShort", "Search")}
                            onChange={(e) => setQuery(e.target.value)}
                            onKeyDown={(e) => {
                                if (e.key === "Escape") {
                                    closeSearch();
                                    e.preventDefault();
                                } else if (e.key === "Enter") {
                                    if (firstHit && firstHit.preset) act.selectPreset(firstHit.layer, firstHit.preset);
                                    else if (firstHit && firstHit.recipe !== design.recipeId) act.selectRecipe(firstHit.recipe);
                                    e.preventDefault();
                                } else if (e.key === "ArrowDown") {
                                    chips()[0]?.focus();
                                    e.preventDefault();
                                }
                                e.stopPropagation();
                            }}
                        />
                        <Btn icon="close" iconSize={10} className={s.clear} title={t("Placemaker.UI.Browser.Clear", "Clear")} onClick={closeSearch} testId="browser-clear" />
                    </div>
                ) : undefined
            }
            right={
                <>
                    {edit && !searching ? (
                        <Btn
                            icon="shuffle"
                            iconSize={12}
                            className={s.headBtn}
                            title={t("Placemaker.UI.Randomize", "Shuffle")}
                            description={t("Placemaker.UI.RandomizeTip", "New variation. Locked values stay.")}
                            onClick={() => act.randomize(edit.step.path)}
                            testId="randomize"
                        />
                    ) : null}
                    <Btn
                        icon="search"
                        iconSize={12}
                        className={s.headBtn}
                        selected={searching}
                        title={t("Placemaker.UI.Browser.Search", "Search types and styles")}
                        onClick={() => (searching ? closeSearch() : setSearching(true))}
                        testId="browser-search-toggle"
                    />
                </>
            }
        />
    );

    const dialog =
        confirm && edit ? (
            <Confirm
                title={confirm === "delete" ? t("Placemaker.UI.Preset.DeleteTitle", "Delete “{0}”?", [editName]) : t("Placemaker.UI.Preset.ResetTitle", "Reset “{0}”?", [editName])}
                message={
                    confirm === "delete"
                        ? t("Placemaker.UI.Preset.DeleteText", "Your style is removed. Builds stay as they are.")
                        : t("Placemaker.UI.Preset.ResetText", "Your changes to this style are dropped.")
                }
                confirm={confirm === "delete" ? t("Placemaker.UI.Delete", "Delete") : t("Placemaker.UI.Preset.Reset", "Reset")}
                danger
                onConfirm={() => {
                    setConfirm(null);
                    act.deletePreset(edit.layer.id, ep ? ep.id : edit.ap.id);
                }}
                onCancel={() => setConfirm(null)}
            />
        ) : null;

    // The save strip, one line at the bottom of the library: where the style you edit stands and what you can do with it.
    // Naming (save as new, rename) and the ⋯ actions take the same line instead of opening below it.
    const strip = !edit ? null : mode === "saveAs" || (mode === "rename" && ep) ? (
        <NameField
            testId={mode === "saveAs" ? "preset-save-as-form" : "preset-rename-form"}
            label={mode === "saveAs" ? t("Placemaker.UI.Preset.NewName", "New style") : t("Placemaker.UI.Preset.Rename", "Rename")}
            initial={mode === "rename" && ep ? ep.name : ep ? t("Placemaker.UI.Preset.CopyName", "{0} (mine)", [editName]) : ""}
            onSave={(n) => {
                if (mode === "saveAs") act.savePresetAs(edit.step.path, n);
                else if (ep) act.renamePreset(edit.layer.id, ep.id, n);
                close();
            }}
            onCancel={close}
        />
    ) : mode === "menu" ? (
        <div className={classNames(s.strip, s.stripWrap)} data-testid="preset-menu">
            {ep?.custom ? (
                <>
                    <MenuItem icon="edit" label={t("Placemaker.UI.Preset.Rename", "Rename")} onClick={() => setMode("rename")} testId="preset-rename" />
                    <MenuItem
                        icon="trash"
                        danger
                        label={t("Placemaker.UI.Delete", "Delete")}
                        onClick={() => {
                            close();
                            setConfirm("delete");
                        }}
                        testId="preset-delete"
                    />
                </>
            ) : null}
            {ep?.overridden ? (
                <MenuItem
                    icon="reset"
                    label={t("Placemaker.UI.Preset.ResetDefault", "Reset to shipped")}
                    onClick={() => {
                        close();
                        setConfirm("reset");
                    }}
                    testId="preset-reset"
                />
            ) : null}
            <MenuItem
                icon="save"
                label={t("Placemaker.UI.Preset.Export", "Export styles")}
                onClick={() => {
                    act.exportPresets();
                    setExported(true);
                    close();
                }}
                testId="preset-export"
            />
            <div className={s.flex} />
            <Btn icon="close" iconSize={11} className={s.stripIcon} title={t("Placemaker.UI.Close", "Close")} onClick={close} testId="preset-menu-close" />
        </div>
    ) : (
        <div
            className={classNames(s.strip, { [s.stripChanged]: changed })}
            data-testid="preset-bar"
            data-modified={changed ? "true" : "false"}
            onKeyDown={(e) => {
                // Enter on a focused button presses that button, not the tool's primary action.
                if (e.key === "Enter") e.stopPropagation();
            }}
        >
            {changed ? (
                <div className={s.modified} data-testid="preset-modified">
                    <div className={s.modDot} />
                    <div className={s.modText} data-ellipsis>
                        {t("Placemaker.UI.Preset.UnsavedChanges", "Unsaved changes")}
                    </div>
                </div>
            ) : (
                <div className={s.stripNote} data-ellipsis>
                    {ep?.overridden
                        ? t("Placemaker.UI.Preset.YourVersion", "Your version of {0}", [editName])
                        : t("Placemaker.UI.Preset.Hint", "Change it below, then save it here")}
                </div>
            )}
            <div className={s.flex} />
            {changed ? (
                <Btn
                    icon="undo"
                    iconSize={12}
                    label={t("Placemaker.UI.Preset.Undo", "Undo")}
                    className={s.stripBtn}
                    disabled={!ep}
                    title={t("Placemaker.UI.Preset.Revert", "Undo changes")}
                    description={t("Placemaker.UI.Preset.RevertTip", "Back to “{0}” as saved", [editName])}
                    onClick={revert}
                    testId="preset-revert"
                />
            ) : null}
            {changed ? (
                <Btn
                    icon="save"
                    iconSize={12}
                    variant={canSave ? "accent" : "ghost"}
                    label={ep?.custom ? t("Placemaker.UI.Save", "Save") : t("Placemaker.UI.Preset.SaveOver", "Save over")}
                    className={s.stripBtn}
                    disabled={!canSave}
                    title={ep?.custom ? t("Placemaker.UI.Preset.SaveTip", "Save changes to this style") : t("Placemaker.UI.Preset.SaveOverTitle", "Save over “{0}”", [editName])}
                    description={
                        !canSave
                            ? t("Placemaker.UI.Preset.CantSave", "This style can't be saved over")
                            : ep?.custom
                              ? undefined
                              : t("Placemaker.UI.Preset.SaveOverTip", "“{0}” uses these settings from now on. Reset it to the shipped one any time (⋯).", [editName])
                    }
                    onClick={() => act.savePreset(edit.step.path, ep ? ep.id : edit.ap.id)}
                    testId="preset-save"
                />
            ) : null}
            <Btn
                icon="plus"
                iconSize={12}
                label={t("Placemaker.UI.Preset.SaveAsShort", "Save as new")}
                className={s.stripBtn}
                title={t("Placemaker.UI.Preset.SaveAsTip", "Save these settings as a new style")}
                onClick={() => setMode("saveAs")}
                testId="preset-save-as"
            />
            <Btn
                icon="more"
                iconSize={12}
                className={s.stripIcon}
                title={t("Placemaker.UI.Preset.More", "More")}
                description={t("Placemaker.UI.Preset.MoreTip", "Rename, delete, reset to shipped, export")}
                onClick={() => setMode("menu")}
                testId="preset-more"
            />
        </div>
    );

    const exportNote =
        edit && exported && design.presetsFile ? (
            <div className={s.exported} data-testid="presets-file">
                <div className={s.exportedHead}>
                    <Icon name="info" size={12} tint="accent" />
                    <div className={s.exportedNote}>{t("Placemaker.UI.Preset.ExportNote", "Saved. Send this file to share your styles.")}</div>
                    <Btn icon="close" iconSize={11} title={t("Placemaker.UI.Close", "Close")} onClick={() => setExported(false)} testId="presets-file-close" />
                </div>
                <div className={s.path} data-testid="presets-file-path">
                    {design.presetsFile}
                </div>
            </div>
        ) : null;

    const color = (r: RecipeInfo) => categoryColor(recipeGroup(r));
    const tIcon = (r: RecipeInfo) => typeIcon(r.id, recipeGroup(r));
    const styleChip = (r: RecipeInfo, layer: LayerType, p: PresetView, inSearch: boolean) => {
        const name = presetName(t, p);
        const active = r.id === design.recipeId && activeId === p.id;
        return (
            <Chip
                key={layer.id + "/" + p.id}
                testId={`preset-${p.id}`}
                nav="preset"
                icon={inSearch && !p.custom ? tIcon(r) : presetIcon(p, name, tIcon(r))}
                color={p.custom ? CUSTOM_COLOR : color(r)}
                name={name}
                on={active}
                nameTestId={active && !inSearch ? "preset-bar-name" : undefined}
                title={inSearch ? recipeName(t, r) + " · " + name : name}
                description={
                    p.custom
                        ? p.basedOn
                            ? t("Placemaker.UI.Preset.BasedOn", "Your style, based on {0}", [humanize(p.basedOn)])
                            : t("Placemaker.UI.Preset.Yours", "Your style")
                        : p.overridden
                          ? t("Placemaker.UI.Preset.EditedTip", "You saved over this style.")
                          : p.id === STANDARD_ID
                            ? t("Placemaker.UI.Preset.StandardTip", "The type as it comes.")
                            : undefined
                }
                extra={{ "data-layer": layer.id }}
                badge={
                    <>
                        {p.overridden ? <div className={s.edited} data-testid={`preset-edited-${p.id}`} /> : null}
                        {active && modified ? <div className={s.dot} /> : null}
                    </>
                }
                onClick={() => pickStyle(r, layer, p)}
            />
        );
    };

    return (
        <div className={s.picker} data-testid="make">
            {head}
            <div className={classNames(s.browser, { [s.browserModified]: changed })} data-testid="type-browser" ref={root} onKeyDown={onKey}>
                {q && !typeHits.length && !styleHits.length ? (
                    <div className={s.empty} data-testid="browser-empty">
                        {t("Placemaker.UI.Browser.None", "Nothing matches “{0}”", [query.trim()])}
                    </div>
                ) : (
                    <>
                        <div className={s.row}>
                            <RowLabel n={1} label={t("Placemaker.UI.Make.Category", "Category")} />
                            <div className={s.chips} data-testid="make-groups">
                                {visibleCats.map((c) => (
                                    <Chip
                                        key={c.id}
                                        testId={`make-group-${c.id}`}
                                        nav="cat"
                                        icon={categoryIcon(c.id)}
                                        color={categoryColor(c.id)}
                                        name={c.title}
                                        count={q ? c.types.filter((r) => typeHits.includes(r)).length : c.types.length}
                                        on={!q && c.id === category}
                                        extra={{ "data-has-selected": c.id === selectedGroup ? "true" : "false" }}
                                        badge={c.id === selectedGroup && c.id !== category && !q ? <div className={s.mark} /> : null}
                                        onClick={() => {
                                            setCategory(c.id);
                                            if (q) closeSearch();
                                            // Switching category picks its first type, so the Style row follows at once.
                                            else if (c.id !== selectedGroup && c.types.length) pickType(c.types[0]);
                                        }}
                                    />
                                ))}
                                {hiddenCats > 0 && moreCats ? (
                                    <button
                                        type="button"
                                        data-nav="cat"
                                        data-testid="make-groups-more"
                                        className={classNames(s.chip, s.chipMore)}
                                        onClick={(e) => {
                                            e.stopPropagation();
                                            setMoreCats(!moreCats);
                                        }}
                                    >
                                        <div className={s.chipName}>
                                            {moreCats ? t("Placemaker.UI.Make.Fewer", "Fewer") : t("Placemaker.UI.Make.MoreN", "{0} more", [hiddenCats])}
                                        </div>
                                        <Icon name={moreCats ? "chevronLeft" : "chevronRight"} size={10} tint="dim" />
                                    </button>
                                ) : null}
                            </div>
                        </div>
                        <div className={s.rule} />
                        <div className={s.row}>
                            <RowLabel n={2} label={t("Placemaker.UI.Make.Type", "Type")} />
                            <div className={s.chips} data-testid="recipes" data-grouped="true">
                                {types.map((r) => {
                                    const layer = typeLayer(catalog, r);
                                    const desc = r.description ? t(r.description, "") : "";
                                    return (
                                        <Chip
                                            key={r.id}
                                            testId={`recipe-${r.id}`}
                                            nav="type"
                                            icon={tIcon(r)}
                                            color={color(r)}
                                            name={recipeName(t, r)}
                                            count={layer ? shownStyles(layer, null).length : undefined}
                                            on={r.id === design.recipeId}
                                            description={desc || undefined}
                                            onClick={() => pickType(r)}
                                        />
                                    );
                                })}
                            </div>
                        </div>
                        <div className={s.rule} />
                        <div className={s.row}>
                            <RowLabel n={3} label={t("Placemaker.UI.Make.Style", "Style")} />
                            {q ? (
                                <div className={s.chips} data-testid="preset-hits">
                                    {styleHits.map((h) => styleChip(h.r, h.layer, h.p, true))}
                                </div>
                            ) : selectedRecipe && selLayer ? (
                                <div className={s.chips} data-testid={`presets-${selectedRecipe.id}`}>
                                    {shownStyles(selLayer, activeId).map((p) => styleChip(selectedRecipe, selLayer, p, false))}
                                </div>
                            ) : (
                                <div className={s.chips} />
                            )}
                        </div>
                    </>
                )}
                {strip}
                {exportNote}
            </div>
            {dialog}
        </div>
    );
};

// ───────────── naming and the more menu ─────────────

const NameField = ({ label, initial, onSave, onCancel, testId }: { label: string; initial: string; onSave: (name: string) => void; onCancel: () => void; testId: string }) => {
    const { t } = useT();
    const [value, setValue] = useState(initial);
    const ok = value.trim().length > 0;
    return (
        <div className={s.strip} data-testid={testId}>
            <div className={s.nameLabel}>{label}</div>
            <input
                className={s.nameInput}
                data-testid="preset-name-input"
                autoFocus
                value={value}
                maxLength={48}
                placeholder={t("Placemaker.UI.Preset.NamePlaceholder", "Style name")}
                onChange={(e) => setValue(e.target.value)}
                onKeyDown={(e) => {
                    e.stopPropagation();
                    if (e.key === "Enter" && ok) onSave(value.trim());
                    if (e.key === "Escape") onCancel();
                }}
            />
            <Btn variant="accent" label={t("Placemaker.UI.Save", "Save")} className={s.stripBtn} disabled={!ok} onClick={() => onSave(value.trim())} testId="preset-name-save" />
            <Btn icon="close" iconSize={11} className={s.stripIcon} title={t("Placemaker.UI.Cancel", "Cancel")} onClick={onCancel} testId="preset-name-cancel" />
        </div>
    );
};

const MenuItem = ({ icon, label, onClick, testId, danger }: { icon: string; label: string; onClick: () => void; testId: string; danger?: boolean }) => (
    <button
        type="button"
        data-testid={testId}
        className={classNames(s.menuItem, { [s.menuDanger]: danger })}
        onClick={(e) => {
            e.stopPropagation();
            onClick();
        }}
    >
        <Icon name={icon} size={11} tint={danger ? "danger" : "dim"} />
        <div className={s.menuText}>{label}</div>
    </button>
);
