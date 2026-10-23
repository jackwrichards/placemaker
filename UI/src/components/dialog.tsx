// Confirmation dialog, in the panel's own style: drawn over the whole panel (its dialog layer, Panel.tsx), centred, the
// panel dimmed behind it, wherever in the panel it is asked for (a button deep in a scrolling list). The game's own
// ConfirmationDialog is not used: inside the panel it had no box and no backdrop (its text over the panel's chips, two
// huge buttons). Outside the panel (no layer) it is drawn where it stands.
import React, { createContext, ReactNode, useContext } from "react";
import { createPortal } from "react-dom";
import { useT } from "../lib/l10n";
import { Btn } from "./common";
import s from "./dialog.module.scss";

type Props = {
    title: ReactNode;
    message: ReactNode;
    confirm: string;
    danger?: boolean;
    onConfirm: () => void;
    onCancel: () => void;
};

/** The element dialogs are drawn into (the panel's dialog layer); null: where they stand. */
export const DialogLayerContext = createContext<HTMLElement | null>(null);

export const OwnDialog = ({ title, message, confirm, danger, onConfirm, onCancel }: Props) => {
    const { t } = useT();
    return (
        <div className={s.backdrop} data-testid="confirm-dialog" onClick={onCancel} onMouseDown={(e) => e.stopPropagation()}>
            <div className={s.dialog} onClick={(e) => e.stopPropagation()}>
                <div className={s.title}>{title}</div>
                <div className={s.message}>{message}</div>
                <div className={s.buttons}>
                    <Btn variant="solid" label={t("Placemaker.UI.Cancel", "Cancel")} onClick={onCancel} testId="confirm-cancel" />
                    <div className={s.spacer} />
                    <Btn variant={danger ? "danger" : "accent"} label={confirm} onClick={onConfirm} testId="confirm-ok" />
                </div>
            </div>
        </div>
    );
};

export const Confirm = (p: Props) => {
    const layer = useContext(DialogLayerContext);
    const dialog = <OwnDialog {...p} />;
    return layer ? createPortal(dialog, layer) : dialog;
};
