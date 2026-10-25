// The style you are editing, right under the Placemaker library and built the same way (the user, 25 Oct: "I've selected
// the formal garden and I'm now editing it"; "it's like a file"): a section line, then one bordered panel.
//   The line: ✎ · the style's name · ● while changed, then words, as for a file: Revert · Save · Save as… and, for your own
//     style, Rename · Delete, or Reset for a built-in you saved over. No icons to guess, no menu. Save as… and Rename turn
//     the line into the name field (Save · Cancel).
//   The panel: everything the style keeps, each part under the same light line (SubHead): Parameters (or Zoning; Shuffle
//     at its end) and Add-ons. Its border turns amber while there are unsaved changes; the lit Style chip carries the same ✎.
import React, { ReactNode, useState } from "react";
import classNames from "classnames";
import { Catalog, DesignState, StepView } from "../contract";
import { act } from "../lib/bindings";
import { useT } from "../lib/l10n";
import { Tip } from "../lib/tips";
import { Btn, Icon } from "../components/common";
import { Confirm } from "../components/dialog";
import { presetName, shownActive } from "./PresetBrowser";
import { SectionHead } from "./SectionHead";
import s from "./styleCard.module.scss";

type Mode = "idle" | "saveAs" | "rename";

/** A part of the card (Parameters, Zoning, Add-ons): icon · TITLE · rule · whatever goes at its end. */
export const SubHead = ({ icon, title, right, testId }: { icon: string; title: string; right?: ReactNode; testId?: string }) => (
    <div className={s.subHead} data-testid={testId}>
        <Icon name={icon} size={11} tint="dim" />
        <div className={s.subTitle}>{title}</div>
        <div className={s.subRule} />
        {right}
    </div>
);

/** Shuffle at the end of the Parameters line: a new variation of the parameters (locked values stay). */
export const ShuffleBtn = ({ step }: { step: StepView }) => {
    const { t } = useT();
    return (
        <Btn
            label={t("Placemaker.UI.Randomize", "Shuffle")}
            className={s.word}
            title={t("Placemaker.UI.Randomize", "Shuffle")}
            description={t("Placemaker.UI.RandomizeTip", "New variation. Locked values stay.")}
            onClick={() => act.randomize(step.path)}
            testId="randomize"
        />
    );
};

const NameInput = ({ initial, onSave, onCancel }: { initial: string; onSave: (name: string) => void; onCancel: () => void }) => {
    const { t } = useT();
    const [value, setValue] = useState(initial);
    const ok = value.trim().length > 0;
    return (
        <div className={s.nameForm}>
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
            <Btn variant="accent" label={t("Placemaker.UI.Save", "Save")} className={s.word} disabled={!ok} onClick={() => onSave(value.trim())} testId="preset-name-save" />
            <Btn label={t("Placemaker.UI.Cancel", "Cancel")} className={s.word} onClick={onCancel} testId="preset-name-cancel" />
        </div>
    );
};

export const StyleCard = ({ catalog, design, step, children }: { catalog: Catalog; design: DesignState; step: StepView; children: ReactNode }) => {
    const { t } = useT();
    const [mode, setMode] = useState<Mode>("idle");
    const [confirm, setConfirm] = useState<"delete" | "reset" | null>(null);
    const ap = design.activePreset;
    const layer = catalog.layers.find((l) => l.id === step.layer);
    if (!ap || !layer) return <div className={s.plain}>{children}</div>;

    // Standard is a style like the others: revert, save over (reset to shipped), save as all work on it.
    const id = shownActive(layer, ap);
    const p = id ? layer.presets.find((x) => x.id === id) ?? null : null;
    const name = p ? presetName(t, p) : t("Placemaker.UI.Preset.Standard", "Standard");
    const changed = !!ap.modified;
    const canSave = changed && ap.canSave && !!p;
    const close = () => setMode("idle");

    const naming = mode === "saveAs" || (mode === "rename" && !!p);
    const head = naming ? (
        <SectionHead
            icon="edit"
            title={mode === "saveAs" ? t("Placemaker.UI.Preset.SaveAs", "Save as") : t("Placemaker.UI.Preset.Rename", "Rename")}
            testId={mode === "saveAs" ? "preset-save-as-form" : "preset-rename-form"}
            fill={
                <NameInput
                    initial={mode === "rename" && p ? p.name : p ? t("Placemaker.UI.Preset.CopyName", "{0} (mine)", [name]) : ""}
                    onSave={(n) => {
                        if (mode === "saveAs") act.savePresetAs(step.path, n);
                        else if (p) act.renamePreset(layer.id, p.id, n);
                        close();
                    }}
                    onCancel={close}
                />
            }
        />
    ) : (
        <SectionHead
            icon="edit"
            title={name}
            tip={p?.overridden ? t("Placemaker.UI.Preset.EditedTip", "You saved over this style.") : name}
            shrink
            testId="preset-bar-head"
            sub={
                changed ? (
                    <Tip title={t("Placemaker.UI.Preset.Unsaved", "Unsaved changes")}>
                        <div className={s.dot} data-testid="preset-modified" />
                    </Tip>
                ) : undefined
            }
            right={
                <div
                    className={s.words}
                    data-testid="preset-bar"
                    data-modified={changed ? "true" : "false"}
                    onKeyDown={(e) => {
                        // Enter on a focused button presses that button, not the tool's primary action.
                        if (e.key === "Enter") e.stopPropagation();
                    }}
                >
                    <Btn
                        label={t("Placemaker.UI.Preset.RevertShort", "Revert")}
                        className={s.word}
                        disabled={!changed || !p}
                        title={t("Placemaker.UI.Preset.Revert", "Undo changes")}
                        description={changed ? t("Placemaker.UI.Preset.RevertTip", "Back to “{0}” as saved", [name]) : t("Placemaker.UI.Preset.NoChanges", "No changes")}
                        onClick={() => changed && p && act.selectPreset(layer.id, p.id)}
                        testId="preset-revert"
                    />
                    <Btn
                        label={t("Placemaker.UI.Save", "Save")}
                        variant={canSave ? "accent" : "ghost"}
                        className={s.word}
                        disabled={!canSave}
                        title={t("Placemaker.UI.Save", "Save")}
                        description={
                            !changed
                                ? t("Placemaker.UI.Preset.NothingToSave", "No changes to save")
                                : !canSave
                                  ? t("Placemaker.UI.Preset.CantSave", "This style can't be saved over")
                                  : p?.custom
                                    ? t("Placemaker.UI.Preset.SaveTip", "Save changes to this style")
                                    : t("Placemaker.UI.Preset.SaveOverTip", "“{0}” uses these settings from now on. Reset brings back the shipped one.", [name])
                        }
                        onClick={() => act.savePreset(step.path, p ? p.id : ap.id)}
                        testId="preset-save"
                    />
                    <Btn
                        label={t("Placemaker.UI.Preset.SaveAsDots", "Save as…")}
                        className={s.word}
                        title={t("Placemaker.UI.Preset.SaveAsTip", "Save these settings as a new style")}
                        onClick={() => setMode("saveAs")}
                        testId="preset-save-as"
                    />
                    {p?.custom ? (
                        <>
                            <Btn label={t("Placemaker.UI.Preset.Rename", "Rename")} className={s.word} onClick={() => setMode("rename")} testId="preset-rename" />
                            <Btn
                                label={t("Placemaker.UI.Delete", "Delete")}
                                className={classNames(s.word, s.wordDanger)}
                                title={t("Placemaker.UI.Preset.DeleteTip", "Delete this style")}
                                onClick={() => setConfirm("delete")}
                                testId="preset-delete"
                            />
                        </>
                    ) : p?.overridden ? (
                        <Btn
                            label={t("Placemaker.UI.Preset.Reset", "Reset")}
                            className={s.word}
                            title={t("Placemaker.UI.Preset.ResetDefault", "Reset to shipped")}
                            description={t("Placemaker.UI.Preset.ResetTip", "Bring back “{0}” as it comes with Placemaker.", [name])}
                            onClick={() => setConfirm("reset")}
                            testId="preset-reset"
                        />
                    ) : null}
                </div>
            }
        />
    );

    return (
        <div className={s.card} data-testid="style-card" data-modified={changed ? "true" : "false"}>
            {head}
            <div className={classNames(s.panel, { [s.panelChanged]: changed })}>{children}</div>
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
