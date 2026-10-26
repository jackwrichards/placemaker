// Style previews (the user, 25 Oct): a photo of the chosen style as it was built, so the player does not have to guess from the
// name. Three views from the same cameras for every style (a cinematic close-up, an isometric-looking view, from above); the view
// picked is remembered for every style after it, and so is the photo folded away to one line (the user: it takes a lot of room). Taken by the game runner (tools/style-previews), shrunk into
// assets/previews/<type>__<style>__<view>.jpg. A style with no photo (the player's own, a new one) shows nothing.
// A new photo fades in over the one before once it has loaded (never a blank box: the user saw it flash and the panel jump),
// the box keeps the last measured size, and the type's photos are loaded ahead.
import React, { useEffect, useRef, useState } from "react";
import classNames from "classnames";
import { useT } from "../lib/l10n";
import { Icon } from "./common";
import { useBoxWidth } from "./SitePlanCard";
import s from "./stylePreview.module.scss";

declare const require: any;

const VIEWS = ["close", "iso", "top"] as const;
type View = (typeof VIEWS)[number];
const VIEW_KEY = "placemaker.previewView.v2";
const OPEN_KEY = "placemaker.previewOpen.v1";

function loadPhotos(): Record<string, string> {
    const out: Record<string, string> = {};
    try {
        const ctx = require.context("../assets/previews", false, /\.jpg$/);
        ctx.keys().forEach((k: string) => {
            const mod = ctx(k);
            out[k.replace(/^\.\//, "").replace(/\.jpg$/, "")] = typeof mod === "string" ? mod : mod?.default ?? "";
        });
    } catch {
        /* no photos bundled */
    }
    return out;
}

const PHOTOS = loadPhotos();

export function photoOf(layer: string, preset: string, view: View): string | undefined {
    return PHOTOS[layer + "__" + preset + "__" + view];
}

function loadView(): View {
    try {
        const v = window.localStorage?.getItem(VIEW_KEY) as View | null;
        return v && (VIEWS as readonly string[]).includes(v) ? v : "iso";
    } catch {
        return "iso";
    }
}

let shared: View | null = null;
let sharedOpen: boolean | null = null;
/** Kept across remounts (another type, another stage): the box width, the photo last on screen. */
let lastW = 0;
let lastSrc = "";
const preloaded = new Set<string>();

/** Loads every photo of a type ahead, so switching its styles and views finds them ready. */
function preload(layer: string) {
    if (preloaded.has(layer)) return;
    preloaded.add(layer);
    for (const [k, url] of Object.entries(PHOTOS)) {
        if (!k.startsWith(layer + "__") || !url) continue;
        try {
            const img = new Image();
            img.src = url;
        } catch {
            /* loads when shown */
        }
    }
}

function loadOpen(): boolean {
    try {
        return window.localStorage?.getItem(OPEN_KEY) !== "0";
    } catch {
        return true;
    }
}

export const StylePreview = ({ layer, preset, name }: { layer: string; preset: string | null; name?: string }) => {
    const { t } = useT();
    // "Playing fields preview" (the user): the style's name with the word
    const note = name ? t("Placemaker.UI.Preview.NoteOf", "{0} preview", [name]) : t("Placemaker.UI.Preview.Note", "Preview");
    const [view, setViewState] = useState<View>(() => shared ?? (shared = loadView()));
    const [open, setOpenState] = useState<boolean>(() => sharedOpen ?? (sharedOpen = loadOpen()));
    // 16:9 (the photos, collect.ps1) from the measured width: the game's view gives an absolute picture no height inside a padding-sized box
    const box = useRef<HTMLDivElement>(null);
    const measured = useBoxWidth(box);
    if (measured) lastW = measured;
    const w = measured || lastW;
    const want = preset ? photoOf(layer, preset, photoOf(layer, preset, view) ? view : VIEWS.find((v) => photoOf(layer, preset, v)) ?? view) ?? "" : "";
    // base: the photo on screen; next: the one fading in over it (shown once loaded, or after 700 ms whatever happens)
    const [base, setBase] = useState(() => lastSrc || want);
    const [next, setNext] = useState("");
    const [nextOn, setNextOn] = useState(false);
    useEffect(() => preload(layer), [layer]);
    useEffect(() => {
        if (!want || want === base) {
            setNext("");
            return;
        }
        setNext(want);
        setNextOn(false);
        const id = window.setTimeout(() => setNextOn(true), 700);
        return () => window.clearTimeout(id);
    }, [want]); // eslint-disable-line react-hooks/exhaustive-deps
    useEffect(() => {
        if (!nextOn || !next) return;
        const id = window.setTimeout(() => {
            setBase(next);
            lastSrc = next;
            setNext("");
            setNextOn(false);
        }, 220);
        return () => window.clearTimeout(id);
    }, [nextOn, next]);
    if (base && !lastSrc) lastSrc = base;
    if (!preset) return null;
    const any = VIEWS.some((v) => photoOf(layer, preset, v));
    if (!any) return null;
    const setView = (v: View) => {
        shared = v;
        setViewState(v);
        try {
            window.localStorage?.setItem(VIEW_KEY, v);
        } catch {
            /* not remembered */
        }
    };
    const setOpen = (o: boolean) => {
        sharedOpen = o;
        setOpenState(o);
        try {
            window.localStorage?.setItem(OPEN_KEY, o ? "1" : "0");
        } catch {
            /* not remembered */
        }
    };
    if (!open)
        return (
            <div className={s.folded} data-testid="style-preview" data-open="0" onClick={() => setOpen(true)}>
                <div className={s.foldedLabel} data-ellipsis="1">{note}</div>
                <Icon name="chevronDown" size={11} tint="dim" />
            </div>
        );
    const shown = photoOf(layer, preset, view) ? view : VIEWS.find((v) => photoOf(layer, preset, v))!;
    const label: Record<View, string> = {
        close: t("Placemaker.UI.Preview.Close", "Close"),
        iso: t("Placemaker.UI.Preview.Iso", "Angle"),
        top: t("Placemaker.UI.Preview.Top", "Above"),
    };
    return (
        <div ref={box} className={s.preview} style={{ height: (w ? Math.round(w * 0.5625) : 200) + "px" }} data-photo={next || base} data-testid="style-preview" data-open="1" data-view={shown}>
            {base ? <img key={"b" + base} className={s.photo} src={base} alt="" /> : null}
            {next ? <img key={"n" + next} className={classNames(s.photo, s.photoNext, { [s.photoOn]: nextOn })} src={next} alt="" onLoad={() => setNextOn(true)} /> : null}
            <div className={s.views}>
                {VIEWS.map((v) => (
                    <div
                        key={v}
                        className={classNames(s.view, { [s.viewOn]: v === shown, [s.viewNone]: !photoOf(layer, preset, v) })}
                        data-testid={"style-preview-" + v}
                        onClick={() => photoOf(layer, preset, v) && setView(v)}
                    >
                        {label[v]}
                    </div>
                ))}
            </div>
            <div className={s.fold} data-testid="style-preview-fold" onClick={() => setOpen(false)}>
                <Icon name="chevronUp" size={11} />
            </div>
            <div className={s.note} data-ellipsis="1">{note}</div>
        </div>
    );
};
