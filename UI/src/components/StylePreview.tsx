// Style previews (the user, 25 Oct): a photo of the chosen style as it was built, so the player does not have to guess from the
// name. Three views from the same cameras for every style (a cinematic close-up, an isometric-looking view, from above); the view
// picked is remembered for every style after it, and so is the photo folded away to one line (the user: it takes a lot of room). Taken by the game runner (tools/style-previews), shrunk into
// assets/previews/<type>__<style>__<view>.jpg. A style with no photo (the player's own, a new one) shows nothing.
import React, { useRef, useState } from "react";
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

function loadOpen(): boolean {
    try {
        return window.localStorage?.getItem(OPEN_KEY) !== "0";
    } catch {
        return true;
    }
}

export const StylePreview = ({ layer, preset }: { layer: string; preset: string | null }) => {
    const { t } = useT();
    const [view, setViewState] = useState<View>(() => shared ?? (shared = loadView()));
    const [open, setOpenState] = useState<boolean>(() => sharedOpen ?? (sharedOpen = loadOpen()));
    // 16:9 from the measured width: the game's view gives an absolute picture no height inside a padding-sized box
    const box = useRef<HTMLDivElement>(null);
    const w = useBoxWidth(box);
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
                <div className={s.foldedLabel}>{t("Placemaker.UI.Preview.Note", "Preview")}</div>
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
        <div ref={box} className={s.preview} style={{ height: (w ? Math.round(w * 0.5625) : 200) + "px" }} data-testid="style-preview" data-open="1" data-view={shown}>
            {/* a new img per photo: the game's view keeps the old picture while a new src loads */}
            <img key={layer + preset + shown} className={s.photo} src={photoOf(layer, preset, shown)} alt="" />
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
            <div className={s.note}>{t("Placemaker.UI.Preview.Note", "Preview")}</div>
        </div>
    );
};
