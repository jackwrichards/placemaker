// s42 — the Place library: pick a place, which is also what you are editing (docs/proposals/s42.md; the Editing bar merged
// in, the user, 25 Oct: "they essentially do the same thing"; "you're like browsing your place library").
//   The section header (SectionHead: PLACEMAKER LIBRARY · search), then one bordered panel,
//     three numbered rows of equal-height chips: 1 Category · 2 Type · 3 Style; the lit chip of each row is what is being
//     edited. Always open (user: no collapse button for it).
//     Each chip = coloured icon tile (category colour; types and styles inherit it) + name + small count. Styles start with
//     Standard (the type's own design, a style like the others: the old "Default"), end with the player's own (gold star).
//     The search icon in the header filters all three rows.
//   The lit Style chip is the style the card under the library edits (StyleCard.tsx: EDITING · the same chip, small); ● while changed.
// Styles are the engine's presets (selectPreset / savePreset / savePresetAs …); categories come from the central shelf
// (Recipes/shelf/<category>.json, Core/Tool/Shelf.cs): the game sends each type's category (RecipeInfo.category), what the
// shelf hides (RecipeInfo.shelved, PresetView.shelved) and the categories in order (Catalog.categories).
import React, { KeyboardEvent, useEffect, useRef, useState } from "react";
import classNames from "classnames";
import { ActivePreset, Catalog, DesignState, LayerType, PresetView, RecipeInfo } from "../contract";
import { act } from "../lib/bindings";
import { humanize, TFunc, useT } from "../lib/l10n";
import { Tip } from "../lib/tips";
import { useOpen } from "../lib/collapse";
import { categoryColor, categoryIcon, CUSTOM_COLOR, styleIcon, typeIcon } from "../lib/palette";
import { Btn, Icon } from "../components/common";
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

/** Standard's name: each type gives its Standard its own (Placemaker.Standard.<layer>, Lang/*.styles.json; the user 25 Oct), else the plain word. */
export const standardName = (t: TFunc, layerId?: string) =>
    (layerId && t("Placemaker.Standard." + layerId, "")) || t("Placemaker.UI.Preset.Standard", "Standard");

export const presetName = (t: TFunc, p: PresetView, layerId?: string) =>
    p.custom
        ? p.name
        : p.id === STANDARD_ID && (!p.name || p.name === "Placemaker.UI.Preset.Standard")
          ? standardName(t, layerId)
          : t(p.name, humanize(p.name || p.id));
const recipeName = (t: TFunc, r: RecipeInfo) => (r.custom ? r.name : t(r.name, humanize(r.name || r.id)));

/** A style's glyph: the player's own a star, Standard the type's own glyph, the others by keyword (lib/palette styleIcon). */
export const presetIcon = (p: PresetView, name: string, typeGlyph: string) =>
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
export function shownActive(layer: LayerType | undefined, ap: ActivePreset | undefined): string | null {
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
    return p ? recipeName(t, r) + " · " + presetName(t, p, layer?.id) : recipeName(t, r);
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

/**
 * The Place library: pick a place, and what you are editing, in one section (the user, 25 Oct: the picker and the Editing
 * bar "essentially do the same thing"): 1 Category · 2 Type · 3 Style, the lit chip of each row is what is being edited.
 * The card under the library (StyleCard.tsx) edits and saves that style. Search (header icon) filters the rows:
 * categories with a hit, the matching types of every category, the matching styles of every type. Arrow keys walk the
 * chips, Enter in the search box picks the first hit, Esc clears it.
 */
export const TypeBrowser = ({ catalog, design }: { catalog: Catalog; design: DesignState }) => {
    const { t } = useT();
    const [query, setQuery] = useState("");
    const [searching, setSearching] = useState(false);
    const [moreCats, setMoreCats] = useOpen("design:moreCategories", false);
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
                const ps = layer ? layer.presets.filter((p) => !p.shelved && has(presetName(t, p, layer.id))) : [];
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

    const head = (
        <SectionHead
            icon="layers"
            title={t("Placemaker.UI.Library.Title", "Placemaker library")}
            first
            testId="make-head"
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

    const color = (r: RecipeInfo) => categoryColor(recipeGroup(r));
    const tIcon = (r: RecipeInfo) => typeIcon(r.id, recipeGroup(r));
    const styleChip = (r: RecipeInfo, layer: LayerType, p: PresetView, inSearch: boolean) => {
        const name = presetName(t, p, layer.id);
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
                        {/* unsaved changes to the style being edited (the card below: ● and an amber frame) */}
                        {active && !inSearch && modified ? <div className={s.dot} /> : null}
                    </>
                }
                onClick={() => pickStyle(r, layer, p)}
            />
        );
    };

    return (
        <div className={s.picker} data-testid="make">
            {head}
            <div className={s.browser} data-testid="type-browser" ref={root} onKeyDown={onKey}>
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
                        {/* the AI builder has no styles: no Style row (the chat is its whole design) */}
                        {selectedRecipe?.id === "ai" && !q ? null : (
                        <>
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
                    </>
                )}
            </div>
        </div>
    );
};
