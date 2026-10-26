// Style previews (the user, 25 Oct): a photo of the chosen style as it was built, so the player does not have to guess from the
// name. Three views from the same cameras for every style (a cinematic close-up, an isometric-looking view, from above); the view
// picked is remembered for every style after it. Taken by the game runner (tools/style-previews), shrunk into
// assets/previews/<type>~<style>~<view>.jpg. A style with no photo (the player's own, a new one) shows nothing.
import React, { useState } from "react";
import classNames from "classnames";
import { useT } from "../lib/l10n";
import s from "./stylePreview.module.scss";

declare const require: any;

const VIEWS = ["close", "iso", "top"] as const;
type View = (typeof VIEWS)[number];
const VIEW_KEY = "placemaker.previewView.v2";

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
    return PHOTOS[layer + "~" + preset + "~" + view];
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

export const StylePreview = ({ layer, preset }: { layer: string; preset: string | null }) => {
    const { t } = useT();
    const [view, setViewState] = useState<View>(() => shared ?? (shared = loadView()));
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
    const shown = photoOf(layer, preset, view) ? view : VIEWS.find((v) => photoOf(layer, preset, v))!;
    const label: Record<View, string> = {
        close: t("Placemaker.UI.Preview.Close", "Close"),
        iso: t("Placemaker.UI.Preview.Iso", "Angle"),
        top: t("Placemaker.UI.Preview.Top", "Above"),
    };
    return (
        <div className={s.preview} data-testid="style-preview" data-view={shown}>
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
            <div className={s.note}>{t("Placemaker.UI.Preview.Note", "Example build")}</div>
        </div>
    );
};
