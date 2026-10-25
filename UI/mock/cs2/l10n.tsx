// Browser stand-in for cs2/l10n: translate() over Lang/en-US*.json (repo files + /api/lang from the
// mock host), and a LocalizedNumber that formats like the game (¢ money, digit grouping).
// ?lang=de-DE shows the panel in that language: Lang/de-DE.*.json over the English, as the game merges them
// (Game/Localization/LocalizationLoader.cs).
import React, { useSyncExternalStore } from "react";
// Every Lang/<locale>.<area>.json in the repo (core, tool, layers, ui), bundled so the mock reads like the game
// even without the host.
declare const require: any;
const langCtx = require.context("../../../Lang", false, /^\.\/[A-Za-z-]+\.[^.]+\.json$/);
const all: Record<string, string> = {};
langCtx.keys().filter((k: string) => k.startsWith("./en-US.")).forEach((k: string) => Object.assign(all, langCtx(k)));
const lang = new URLSearchParams(location.search).get("lang") ?? "en-US";
const own: Record<string, string> = {};
if (lang !== "en-US") langCtx.keys().filter((k: string) => k.startsWith("./" + lang + ".")).forEach((k: string) => Object.assign(own, langCtx(k)));
Object.assign(all, own);
import uiStrings from "../../../Lang/en-US.ui.json";

let dict: Record<string, string> = { ...all };
let version = 0;
const listeners = new Set<() => void>();

export function addStrings(more: Record<string, string>) {
    dict = { ...more, ...dict, ...(uiStrings as Record<string, string>) };
    // Layer-owned files (from the host) win for their own keys.
    for (const k of Object.keys(more)) if (!k.startsWith("Placemaker.UI.")) dict[k] = more[k];
    Object.assign(dict, own);
    version++;
    listeners.forEach((l) => l());
}

/** Mock-only: show raw keys instead of English to spot missing localisation. */
export let showKeys = false;
export function setShowKeys(v: boolean) {
    showKeys = v;
    version++;
    listeners.forEach((l) => l());
}

const translate = (id: string, fallback?: string | null) => {
    if (showKeys) return id;
    const v = dict[id];
    return v !== undefined ? v : fallback ?? null;
};

export function useLocalization() {
    useSyncExternalStore(
        (l) => {
            listeners.add(l);
            return () => listeners.delete(l);
        },
        () => version,
    );
    return { translate, unitSettings: {} };
}

export const useCachedLocalization = useLocalization;

function group(n: number) {
    return Math.round(n)
        .toString()
        .replace(/\B(?=(\d{3})+(?!\d))/g, ",");
}

export const LocalizedNumber = ({ value, unit }: { value: number; unit?: string }) => {
    switch (unit) {
        case "money":
            return <>{"¢" + group(value)}</>;
        case "moneyPerMonth":
            return <>{"¢" + group(value) + "/month"}</>;
        default:
            return <>{group(value)}</>;
    }
};

export const Unit = { Money: "money", MoneyPerMonth: "moneyPerMonth", Integer: "integer" };
