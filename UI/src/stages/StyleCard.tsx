// The style you are editing, as one card right under the Placemaker library (the user, 25 Oct: "I've selected the formal
// garden and I'm now editing it ... wrap the bottom stuff in it"). Its head is the lit Style chip again, bigger: ✎ · the
// style's tile · its name · ● while changed; then undo (↶) · Save over (a built-in, reset to shipped any time) or Save (your
// own) · Save as new · shuffle · ⋯ (rename, delete, reset to shipped, export). Naming and the ⋯ actions take the head's
// line. Its body is everything the style keeps: Parameters (or Zoning) and Add-ons, each under the same light line
// (SubHead). The lit Style chip carries the same ✎, so the two read as one thing.
import React, { ReactNode, useState } from "react";
import classNames from "classnames";
import { Catalog, DesignState, StepView } from "../contract";
import { act } from "../lib/bindings";
import { useT } from "../lib/l10n";
import { Tip } from "../lib/tips";
import { categoryColor, CUSTOM_COLOR, typeIcon } from "../lib/palette";
import { Btn, Icon } from "../components/common";
import { Confirm } from "../components/dialog";
import { presetIcon, presetName, recipeGroup, shownActive, Tile } from "./PresetBrowser";
import s from "./styleCard.module.scss";

type Mode = "idle" | "saveAs" | "rename" | "menu";

/** A part of the card (Parameters, Zoning, Add-ons): icon · TITLE · rule · whatever goes at its end. */
export const SubHead = ({ icon, title, right, testId }: { icon: string; title: string; right?: ReactNode; testId?: string }) => (
    <div className={s.subHead} data-testid={testId}>
        <Icon name={icon} size={11} tint="dim" />
        <div className={s.subTitle}>{title}</div>
        <div className={s.subRule} />
        {right}
    </div>
);

const NameField = ({ label, initial, onSave, onCancel, testId }: { label: string; initial: string; onSave: (name: string) => void; onCancel: () => void; testId: string }) => {
    const { t } = useT();
    const [value, setValue] = useState(initial);
    const ok = value.trim().length > 0;
    return (
        <div className={s.head} data-testid={testId}>
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
            <Btn variant="accent" label={t("Placemaker.UI.Save", "Save")} className={s.btn} disabled={!ok} onClick={() => onSave(value.trim())} testId="preset-name-save" />
            <Btn icon="close" iconSize={11} className={s.icon} title={t("Placemaker.UI.Cancel", "Cancel")} onClick={onCancel} testId="preset-name-cancel" />
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

export const StyleCard = ({ catalog, design, step, children }: { catalog: Catalog; design: DesignState; step: StepView; children: ReactNode }) => {
    const { t } = useT();
    const [mode, setMode] = useState<Mode>("idle");
    const [confirm, setConfirm] = useState<"delete" | "reset" | null>(null);
    const [exported, setExported] = useState(false);
    const ap = design.activePreset;
    const layer = catalog.layers.find((l) => l.id === step.layer);
    if (!ap || !layer) return <div className={s.plain}>{children}</div>;

    // Standard is a style like the others: undo, save over (reset to shipped), save as new all work on it.
    const recipe = catalog.recipes.find((r) => r.id === design.recipeId);
    const id = shownActive(layer, ap);
    const p = id ? layer.presets.find((x) => x.id === id) ?? null : null;
    const name = p ? presetName(t, p) : t("Placemaker.UI.Preset.Standard", "Standard");
    const group = recipe ? recipeGroup(recipe) : "other";
    const tIcon = typeIcon(layer.id, group);
    const changed = !!ap.modified;
    const canSave = changed && ap.canSave && !!p;
    const close = () => setMode("idle");

    const head =
        mode === "saveAs" || (mode === "rename" && p) ? (
            <NameField
                testId={mode === "saveAs" ? "preset-save-as-form" : "preset-rename-form"}
                label={mode === "saveAs" ? t("Placemaker.UI.Preset.NewName", "New style") : t("Placemaker.UI.Preset.Rename", "Rename")}
                initial={mode === "rename" && p ? p.name : p ? t("Placemaker.UI.Preset.CopyName", "{0} (mine)", [name]) : ""}
                onSave={(n) => {
                    if (mode === "saveAs") act.savePresetAs(step.path, n);
                    else if (p) act.renamePreset(layer.id, p.id, n);
                    close();
                }}
                onCancel={close}
            />
        ) : mode === "menu" ? (
            <div className={classNames(s.head, s.headWrap)} data-testid="preset-menu">
                {p?.custom ? (
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
                {p?.overridden ? (
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
                <Btn icon="close" iconSize={11} className={s.icon} title={t("Placemaker.UI.Close", "Close")} onClick={close} testId="preset-menu-close" />
            </div>
        ) : (
            <div
                className={s.head}
                data-testid="preset-bar"
                data-modified={changed ? "true" : "false"}
                onKeyDown={(e) => {
                    // Enter on a focused button presses that button, not the tool's primary action.
                    if (e.key === "Enter") e.stopPropagation();
                }}
            >
                <Icon name="edit" size={12} tint="accent" />
                <div className={s.tile}>
                    <Tile icon={p ? presetIcon(p, name, tIcon) : tIcon} color={p?.custom ? CUSTOM_COLOR : categoryColor(group)} size={18} />
                </div>
                <Tip title={name} description={p?.overridden && !changed ? t("Placemaker.UI.Preset.EditedTip", "You saved over this style.") : undefined}>
                    <div className={s.name} data-testid="preset-bar-name" data-ellipsis>
                        {name}
                    </div>
                </Tip>
                {changed ? (
                    <Tip title={t("Placemaker.UI.Preset.Unsaved", "Unsaved changes")}>
                        <div className={s.dot} data-testid="preset-modified" />
                    </Tip>
                ) : null}
                <div className={s.flex} />
                <Btn
                    icon="undo"
                    iconSize={12}
                    className={s.icon}
                    disabled={!changed || !p}
                    title={t("Placemaker.UI.Preset.Revert", "Undo changes")}
                    description={changed ? t("Placemaker.UI.Preset.RevertTip", "Back to “{0}” as saved", [name]) : t("Placemaker.UI.Preset.NoChanges", "No changes")}
                    onClick={() => changed && p && act.selectPreset(layer.id, p.id)}
                    testId="preset-revert"
                />
                <Btn
                    icon="save"
                    iconSize={12}
                    variant={canSave ? "accent" : "ghost"}
                    label={p?.custom ? t("Placemaker.UI.Save", "Save") : t("Placemaker.UI.Preset.SaveOver", "Save over")}
                    className={s.btn}
                    disabled={!canSave}
                    title={p?.custom ? t("Placemaker.UI.Preset.SaveTip", "Save changes to this style") : t("Placemaker.UI.Preset.SaveOverTitle", "Save over “{0}”", [name])}
                    description={
                        !changed
                            ? t("Placemaker.UI.Preset.NothingToSave", "No changes to save")
                            : !canSave
                              ? t("Placemaker.UI.Preset.CantSave", "This style can't be saved over")
                              : p?.custom
                                ? undefined
                                : t("Placemaker.UI.Preset.SaveOverTip", "“{0}” uses these settings from now on. Reset it to the shipped one any time (⋯).", [name])
                    }
                    onClick={() => act.savePreset(step.path, p ? p.id : ap.id)}
                    testId="preset-save"
                />
                <Btn
                    icon="plus"
                    iconSize={12}
                    label={t("Placemaker.UI.Preset.SaveAsShort", "Save as new")}
                    className={s.btn}
                    title={t("Placemaker.UI.Preset.SaveAsTip", "Save these settings as a new style")}
                    onClick={() => setMode("saveAs")}
                    testId="preset-save-as"
                />
                <Btn
                    icon="shuffle"
                    iconSize={12}
                    className={s.icon}
                    title={t("Placemaker.UI.Randomize", "Shuffle")}
                    description={t("Placemaker.UI.RandomizeTip", "New variation. Locked values stay.")}
                    onClick={() => act.randomize(step.path)}
                    testId="randomize"
                />
                <Btn
                    icon="more"
                    iconSize={12}
                    className={s.icon}
                    title={t("Placemaker.UI.Preset.More", "More")}
                    description={t("Placemaker.UI.Preset.MoreTip", "Rename, delete, reset to shipped, export")}
                    onClick={() => setMode("menu")}
                    testId="preset-more"
                />
            </div>
        );

    return (
        <div className={classNames(s.card, { [s.cardChanged]: changed })} data-testid="style-card" data-modified={changed ? "true" : "false"}>
            {head}
            {exported && design.presetsFile ? (
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
            ) : null}
            <div className={s.body}>{children}</div>
            {confirm ? (
                <Confirm
                    title={confirm === "delete" ? t("Placemaker.UI.Preset.DeleteTitle", "Delete “{0}”?", [name]) : t("Placemaker.UI.Preset.ResetTitle", "Reset “{0}”?", [name])}
                    message={
                        confirm === "delete"
                            ? t("Placemaker.UI.Preset.DeleteText", "Your style is removed. Builds stay as they are.")
                            : t("Placemaker.UI.Preset.ResetText", "Your changes to this style are dropped.")
                    }
                    confirm={confirm === "delete" ? t("Placemaker.UI.Delete", "Delete") : t("Placemaker.UI.Preset.Reset", "Reset")}
                    danger
                    onConfirm={() => {
                        setConfirm(null);
                        act.deletePreset(layer.id, p ? p.id : ap.id);
                    }}
                    onCancel={() => setConfirm(null)}
                />
            ) : null}
        </div>
    );
};
