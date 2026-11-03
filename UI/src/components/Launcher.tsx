// Toolbar launcher (GameTopLeft) — one click = toggle the tool; selected state follows state.open, so
// it stays in sync with the hotkey and Esc. Always mounted, so it sits behind its own ErrorBoundary
// with a null fallback: it must never take the top bar down with it.
import React from "react";
import { act, useToolState } from "../lib/bindings";
import { useT } from "../lib/l10n";
import { icon } from "../lib/icons";
import { ErrorBoundary } from "../lib/errorBoundary";
import { SafeFloatingButton } from "../lib/safeButton";
import { Tip } from "../lib/tips";
import { Keys } from "./common";
import s from "./launcher.module.scss";

const LauncherInner = () => {
    const { t } = useT();
    const state = useToolState();
    return (
        <Tip
            direction="down"
            title={t("Placemaker.Name", "Placemaker")}
            description={t("Placemaker.UI.LauncherTip", "Choose an area and turn it into a park, plaza, parking lot, streets… or your own mix.")}
            footer={<Keys keys={["Ctrl", "Shift", "A"]} />}
        >
            <div className={s.wrap} data-testid="launcher" data-open={state.open ? "true" : "false"}>
                <SafeFloatingButton src={icon("logo")} selected={state.open && !state.painter?.tool && !state.painter?.outgrowMode} onSelect={act.toggleTool} className={s.button} />
            </div>
        </Tip>
    );
};

/** The neighbourhood-polish tool's own button (same mod, same tool underneath, its own small panel). */
const PolishLauncherInner = () => {
    const { t } = useT();
    const state = useToolState();
    const on = state.open && (!!state.painter?.tool || !!state.painter?.outgrowMode);
    return (
        <Tip
            direction="down"
            title={t("Placemaker.UI.Polish.Name", "Outgrow")}
            description={t("Placemaker.UI.Polish.LauncherTip", "Pick buildings and outgrow their gardens: shared fences and hedges, lawns or painted ground, planting, by theme.")}
        >
            <div className={s.wrap} data-testid="polish-launcher" data-open={on ? "true" : "false"}>
                <SafeFloatingButton src={icon("shrub")} selected={on} onSelect={act.togglePolish} className={s.button} />
            </div>
        </Tip>
    );
};

export const PolishLauncher = () => (
    <ErrorBoundary name="polish-launcher" fallback={null}>
        <PolishLauncherInner />
    </ErrorBoundary>
);

export const Launcher = () => (
    <ErrorBoundary name="launcher" fallback={null}>
        <LauncherInner />
    </ErrorBoundary>
);
