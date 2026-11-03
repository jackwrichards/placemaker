// Asset picker: a large thumbnail grid (surfaces read as texture swatches), category / theme tabs,
// search, "Automatic" tile, recently used, multi-select.
// Opened by Asset / AssetMulti / Arrangement controls through PickerContext and rendered by the panel
// as a flyout beside it (never clipped by the scrolling body). Closes with its X, Esc or a click outside.
// Hovering a tile previews it live on the map (req.onPreview → previewValue); leaving the grid or
// closing without choosing ends the preview (req.onPreviewEnd); a click commits (req.onPick → setValue).
import React, { createContext, useContext, useEffect, useMemo, useRef, useState } from "react";
import classNames from "classnames";
import { AssetItem } from "../contract";
import { act, useAssets } from "../lib/bindings";
import { humanize, TFunc, useT } from "../lib/l10n";
import { compactMoney } from "../lib/format";
import { Tip } from "../lib/tips";
import { Btn, Icon, Keys } from "./common";
import s from "./picker.module.scss";
import { ScrollArea } from "./ScrollArea";

export interface PickerRequest {
    category: string;
    title: string;
    /** single: pick one id ("" = automatic). multi: toggle ids in a set. append: add one id (arrangement). */
    mode: "single" | "multi" | "append";
    selected: string[];
    onPick: (ids: string[]) => void;
    allowAutomatic: boolean;
    /** s16: what Automatic places right now (the latest plan's pick), shown on the Automatic tile. */
    autoItem?: AssetItem;
    /** Live map preview of what the value would become if the hovered tile were clicked. */
    onPreview?: (ids: string[]) => void;
    /** Stop previewing: the value goes back to `committed` (what is actually chosen now). */
    onPreviewEnd?: (committed: string[]) => void;
}

export const PickerContext = createContext<{ open: (r: PickerRequest) => void; close: () => void; current: PickerRequest | null }>({
    open: () => undefined,
    close: () => undefined,
    current: null,
});

export const usePicker = () => useContext(PickerContext);

const RECENT_KEY = "placemaker.recentAssets";

function loadRecent(): Record<string, string[]> {
    try {
        const raw = window.localStorage?.getItem(RECENT_KEY);
        const v = raw ? JSON.parse(raw) : {};
        return v && typeof v === "object" ? v : {};
    } catch {
        return {};
    }
}

function pushRecent(category: string, id: string) {
    if (!id) return;
    try {
        const all = loadRecent();
        const list = [id, ...(all[category] ?? []).filter((x) => x !== id)].slice(0, 8);
        all[category] = list;
        window.localStorage?.setItem(RECENT_KEY, JSON.stringify(all));
    } catch {
        /* storage unavailable */
    }
}

// ───────────── categories ─────────────

export const isSurface = (category: string | undefined) => !!category && (category === "surface" || category.startsWith("surface."));

/**
 * The list a control's picker browses: every surface for any surface.* control (so the Grass / Paving /
 * Natural / Asphalt tabs are all there, opened on the control's own kind), else the category itself.
 */
export const listCategoryOf = (category: string | undefined) => (isSurface(category) ? "surface" : category ?? "");

/** First items of the automatic pool (C# tags eligible items "auto:<category>"), for the Automatic mosaic. */
export function autoPool(list: AssetItem[] | undefined, category: string | undefined, n = 3): AssetItem[] {
    if (!list || !list.length) return [];
    const cat = category ?? "";
    const has = (a: AssetItem, tag: string) => (a.tags ?? []).includes(tag);
    let pool = list.filter((a) => has(a, "auto:" + cat));
    if (!pool.length && cat !== listCategoryOf(cat)) pool = list.filter((a) => has(a, cat));
    if (!pool.length) pool = list;
    return pool.slice(0, n);
}

interface Tab {
    id: string;
    label: string;
    match: (a: AssetItem) => boolean;
}

const surfaceTabs = (t: TFunc): [string, string][] => [
    ["surface.grass", t("Placemaker.UI.Surface.Grass", "Grass")],
    ["surface.paving", t("Placemaker.UI.Surface.Paving", "Paving")],
    ["surface.natural", t("Placemaker.UI.Surface.Natural", "Natural")],
    ["surface.asphalt", t("Placemaker.UI.Surface.Asphalt", "Asphalt")],
];

/**
 * Tabs over a list: surfaces by kind (tags surface.grass…), anything else by "theme:<name>" tags
 * (decals: leaves, cracks, markings…). Only tabs with items; none at all when fewer than two.
 */
export function tabsFor(category: string, list: AssetItem[], t: TFunc): Tab[] {
    let tabs: Tab[];
    if (isSurface(category)) {
        tabs = surfaceTabs(t).map(([id, label]) => ({ id, label, match: (a: AssetItem) => (a.tags ?? []).includes(id) }));
    } else {
        const themes: string[] = [];
        for (const a of list) for (const tag of a.tags ?? []) if (tag.startsWith("theme:") && !themes.includes(tag)) themes.push(tag);
        tabs = themes.map((tag) => {
            const name = tag.slice("theme:".length);
            return { id: tag, label: t("Placemaker.UI.Theme." + name, humanize(name)), match: (a: AssetItem) => (a.tags ?? []).includes(tag) };
        });
    }
    tabs = tabs.filter((tab) => list.some(tab.match));
    if (tabs.length < 2) return [];
    return [{ id: "", label: t("Placemaker.UI.Picker.TabAll", "All"), match: () => true }, ...tabs];
}

// ───────────── names & lists ─────────────

/** Display name for an asset id, or a prettified id until the list arrives. */
export function useAssetName() {
    const assets = useAssets();
    return (category: string | undefined, id: string) =>
        assets[category ?? ""]?.find((a) => a.id === id)?.name ?? assets[listCategoryOf(category)]?.find((a) => a.id === id)?.name ?? prettifyId(id);
}

export function prettifyId(id: string) {
    return id.replace(/[_]+/g, " ").replace(/([a-z])([A-Z])/g, "$1 $2").replace(/\s+\d+$/, "").trim();
}

/** The list a control browses (requested once, on demand); undefined while loading. */
export function useAssetList(category: string | undefined): AssetItem[] | undefined {
    const assets = useAssets();
    const requested = useRef("");
    const cat = category ? listCategoryOf(category) : "";
    const list = cat ? assets[cat] : undefined;
    useEffect(() => {
        if (cat && !list && requested.current !== cat) {
            requested.current = cat;
            act.requestAssets(cat);
        }
    }, [cat, list]);
    return list;
}

// ───────────── thumbnails ─────────────

function initialsOf(name: string | undefined) {
    return (name || "?")
        .split(/[\s_-]+/)
        .filter((w) => w)
        .slice(0, 2)
        .map((w) => w.charAt(0))
        .join("")
        .toUpperCase();
}

/**
 * An asset's thumbnail. No image, or one that fails to load → a neutral swatch with initials (never
 * an empty box).
 */
export const Thumb = ({ item, size = 34, testId, className }: { item?: AssetItem; size?: number; testId?: string; className?: string }) => {
    const [failed, setFailed] = useState("");
    const style = { width: size + "rem", height: size + "rem" };
    const src = item?.icon ?? "";
    if (src && failed !== src)
        return <img className={classNames(s.thumbImg, className)} style={style} src={src} data-testid={testId} data-thumb="img" onError={() => setFailed(src)} />;
    return (
        <div
            className={classNames(s.thumbBlank, className)}
            style={{ ...style, fontSize: Math.max(8, Math.round(size * 0.34)) + "rem" }}
            data-testid={testId}
            data-thumb="initials"
        >
            {initialsOf(item?.name || item?.id)}
        </div>
    );
};

/** "Automatic": the first three pool thumbnails in a 2×2 mosaic, the fourth cell the auto glyph. */
export const AutoMosaic = ({ items, size = 28, testId }: { items: AssetItem[]; size?: number; testId?: string }) => {
    const cell = Math.floor(size / 2);
    const cellStyle = { width: cell + "rem", height: cell + "rem" };
    const boxStyle = { width: cell * 2 + "rem", height: cell * 2 + "rem" };
    if (!items.length)
        return (
            <div className={s.mosaicEmpty} style={boxStyle} data-testid={testId} data-thumb="auto">
                <Icon name="auto" size={Math.round(size * 0.55)} tint="accent" />
            </div>
        );
    return (
        <div className={s.mosaic} style={boxStyle} data-testid={testId} data-thumb="mosaic">
            {[0, 1, 2].map((i) =>
                items[i] ? <Thumb key={i} item={items[i]} size={cell} className={s.mosaicCell} /> : <div key={i} className={s.mosaicCell} style={cellStyle} />,
            )}
            <div className={classNames(s.mosaicCell, s.mosaicAuto)} style={cellStyle}>
                <Icon name="auto" size={Math.max(8, cell - 4)} tint="accent" />
            </div>
        </div>
    );
};

// ───────────── picker card ─────────────

/** How long the pointer may sit between tiles before the map preview is dropped (avoids flicker). */
const LEAVE_MS = 160;

export const AssetPickerCard = ({ req, onClose }: { req: PickerRequest; onClose: () => void }) => {
    const { t } = useT();
    const list = useAssetList(req.category);
    const pool = useMemo(() => autoPool(list, req.category, 3), [list, req.category]);
    const [query, setQuery] = useState("");
    const [selected, setSelected] = useState<string[]>(req.selected);
    useEffect(() => setSelected(req.selected), [req]);
    const surface = isSurface(req.category);
    const tabs = useMemo(() => tabsFor(req.category, list ?? [], t), [req.category, list, t]);
    const [tab, setTab] = useState<string | null>(null);
    // A surface control opens on its own kind (Paving for a plaza); everything else on "All".
    const activeTab = tab ?? (tabs.some((x) => x.id === req.category) ? req.category : "");
    const recentIds = useMemo(() => loadRecent()[req.category] ?? [], [req.category]);

    // ── live map preview while hovering ──
    const live = useRef({ req, selected, previewing: false, hovered: null as string | null, timer: 0 });
    live.current.req = req;
    live.current.selected = selected;
    const clearTimer = () => {
        if (live.current.timer) window.clearTimeout(live.current.timer);
        live.current.timer = 0;
    };
    const endPreview = () => {
        clearTimer();
        const l = live.current;
        l.hovered = null;
        if (!l.previewing) return;
        l.previewing = false;
        l.req.onPreviewEnd?.(l.selected);
    };
    const nextFor = (id: string) =>
        req.mode === "multi" ? (id === "" ? [] : selected.includes(id) ? selected.filter((x) => x !== id) : [...selected, id]) : id === "" ? [] : [id];
    const hover = (id: string) => {
        if (!req.onPreview || req.mode === "append") return;
        clearTimer();
        if (live.current.hovered === id) return;
        live.current.hovered = id;
        live.current.previewing = true;
        req.onPreview(nextFor(id));
    };
    const unhover = () => {
        if (!live.current.previewing) return;
        clearTimer();
        live.current.timer = window.setTimeout(endPreview, LEAVE_MS);
    };
    // Closing without choosing (X, Esc, click outside, stage change) ends the preview.
    useEffect(() => () => endPreview(), []);

    // Esc closes the picker wherever the focus is (captured first, so it doesn't also close the tool).
    useEffect(() => {
        const onKey = (e: KeyboardEvent) => {
            if (e.key !== "Escape") return;
            e.preventDefault();
            e.stopPropagation();
            e.stopImmediatePropagation?.();
            onClose();
        };
        window.addEventListener("keydown", onKey, true);
        return () => window.removeEventListener("keydown", onKey, true);
    }, [onClose]);

    const q = query.trim().toLowerCase();
    const tabMatch = tabs.find((x) => x.id === activeTab)?.match ?? (() => true);
    const items = (list ?? []).filter((a) =>
        q ? a.name.toLowerCase().includes(q) || a.id.toLowerCase().includes(q) || (a.tags ?? []).some((x) => x.toLowerCase().includes(q)) : tabMatch(a),
    );
    const recent = q ? [] : recentIds.map((id) => list?.find((a) => a.id === id)).filter((a): a is AssetItem => !!a && tabMatch(a));

    const pick = (id: string) => {
        // The commit (setValue) replaces the preview: nothing to end afterwards.
        clearTimer();
        live.current.previewing = false;
        live.current.hovered = null;
        if (req.mode === "multi") {
            const next = nextFor(id);
            setSelected(next);
            pushRecent(req.category, id);
            req.onPick(next);
            return;
        }
        pushRecent(req.category, id);
        req.onPick(id === "" ? [] : [id]);
        onClose();
    };

    const thumbSize = surface ? 78 : 60;
    const tile = (a: AssetItem, keyPrefix = "") => {
        const on = selected.includes(a.id);
        return (
            <button
                type="button"
                key={keyPrefix + a.id}
                data-testid={`asset-${a.id}`}
                className={classNames(s.tile, { [s.tileOn]: on, [s.tileSwatch]: surface })}
                onMouseEnter={() => hover(a.id)}
                onMouseLeave={unhover}
                onClick={(e) => {
                    e.stopPropagation();
                    pick(a.id);
                }}
            >
                <Thumb item={a} size={thumbSize} />
                <div className={s.tileName}>{a.name}</div>
                {a.cost ? <div className={s.tileCost}>{compactMoney(a.cost)}</div> : null}
                {on ? (
                    <div className={s.tileCheck}>
                        <Icon name="check" size={12} tint="dark" />
                    </div>
                ) : null}
            </button>
        );
    };

    const hint =
        req.mode === "multi"
            ? selected.length
                ? t("Placemaker.UI.Picker.SelectedN", "{0} selected", [selected.length])
                : t("Placemaker.UI.Picker.AutoMixHint", "None: automatic mix")
            : req.onPreview
            ? t("Placemaker.UI.Picker.HoverHint", "Hover to preview, click to use")
            : "";

    return (
        <div className={s.card} data-testid="asset-picker" data-category={req.category} onMouseDown={(e) => e.stopPropagation()}>
            <div className={s.head}>
                <div className={s.headRow}>
                    <div className={s.title}>{req.title}</div>
                    <Tip title={t("Placemaker.UI.Close", "Close")} description={<Keys keys={["Esc"]} />}>
                        <button
                            type="button"
                            className={s.closeBtn}
                            data-testid="asset-picker-close"
                            data-icon-button="true"
                            onClick={(e) => {
                                e.stopPropagation();
                                onClose();
                            }}
                        >
                            <Icon name="close" size={13} />
                        </button>
                    </Tip>
                </div>
                <div className={s.search}>
                    <Icon name="search" size={12} tint="dim" />
                    <input
                        className={s.searchInput}
                        data-testid="asset-search"
                        value={query}
                        placeholder={t("Placemaker.UI.Picker.Search", "Search")}
                        onChange={(e) => setQuery(e.target.value)}
                        onKeyDown={(e) => {
                            e.stopPropagation();
                            if (e.key === "Escape") onClose();
                        }}
                    />
                </div>
                {tabs.length && !q ? (
                    <div className={s.tabs} data-testid="asset-tabs">
                        {tabs.map((x) => (
                            <button
                                type="button"
                                key={x.id || "all"}
                                data-testid={`asset-tab-${x.id || "all"}`}
                                aria-pressed={x.id === activeTab ? "true" : "false"}
                                className={classNames(s.tab, { [s.tabOn]: x.id === activeTab })}
                                onClick={(e) => {
                                    e.stopPropagation();
                                    setTab(x.id);
                                }}
                            >
                                <div className={s.tabText}>{x.label}</div>
                            </button>
                        ))}
                    </div>
                ) : null}
            </div>
            <ScrollArea className={s.body} testId="asset-grid" onMouseLeave={unhover}>
                {list === undefined ? (
                    <div className={s.empty}>{t("Placemaker.UI.Picker.Loading", "Loading assets…")}</div>
                ) : (
                    <>
                        {recent.length ? (
                            <>
                                <div className={s.section}>{t("Placemaker.UI.Picker.Recent", "Recent")}</div>
                                <div className={s.grid}>{recent.map((a) => tile(a, "r-"))}</div>
                            </>
                        ) : null}
                        <div className={s.section}>
                            {q
                                ? t("Placemaker.UI.Picker.Results", "{0} results", [items.length])
                                : t("Placemaker.UI.Picker.All", "All ({0})", [items.length])}
                        </div>
                        <div className={s.grid}>
                            {req.allowAutomatic && !q ? (
                                <button
                                    type="button"
                                    data-testid="asset-automatic"
                                    className={classNames(s.tile, s.autoTile, { [s.tileOn]: selected.length === 0, [s.tileSwatch]: surface })}
                                    onMouseEnter={() => hover("")}
                                    onMouseLeave={unhover}
                                    onClick={(e) => {
                                        e.stopPropagation();
                                        pick("");
                                    }}
                                >
                                    {req.autoItem ? <Thumb item={req.autoItem} size={thumbSize} /> : <AutoMosaic items={pool} size={thumbSize} />}
                                    <div className={s.tileName}>
                                        {req.mode === "multi"
                                            ? t("Placemaker.UI.Picker.AutoMix", "Auto mix")
                                            : req.autoItem
                                              ? t("Placemaker.UI.Picker.AutoIs", "Automatic · {0}", [req.autoItem.name || req.autoItem.id])
                                              : t("Placemaker.UI.Picker.Auto", "Automatic")}
                                    </div>
                                </button>
                            ) : null}
                            {items.map((a) => tile(a))}
                        </div>
                        {items.length === 0 ? <div className={s.empty}>{t("Placemaker.UI.Picker.None", "Nothing matches.")}</div> : null}
                    </>
                )}
            </ScrollArea>
            {hint || req.mode === "multi" ? (
                <div className={s.foot}>
                    <div className={s.footInfo}>{hint}</div>
                    {req.mode === "multi" ? <Btn variant="accent" label={t("Placemaker.UI.Done", "Done")} onClick={onClose} testId="asset-picker-done" /> : null}
                </div>
            ) : null}
        </div>
    );
};
