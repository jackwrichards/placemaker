// (P2-1) A car park's own rows, in the game's info panel and in Manage: how full it is and its parking fee.
import React, { useState } from "react";
import { act } from "../lib/bindings";
import { useT } from "../lib/l10n";
import { Switch } from "./common";
import { Slider } from "../controls/Slider";
import s from "./infoSection.module.scss";

/**
 * (P2-1) The parking fee of a placed car park, as Parking Lot Tool has it: a switch (off keeps the amount) and the amount per park.
 * Applies at once, no rebuild (C# ParkingEconomySystem writes it on every stall's parking lane).
 */
export const FeeRow = ({ id, fee, on }: { id: number; fee: number; on: boolean }) => {
    const { t } = useT();
    const [shown, setShown] = useState(fee > 0 ? fee : 10);
    return (
        <div className={s.fee} data-testid="info-fee">
            <div className={s.feeHead}>
                <div className={s.feeLabel}>{t("Placemaker.UI.Info.Fee", "Parking fee")}</div>
                <Switch testId="info-fee-on" on={on} onChange={(v) => act.setGroupFeeOn(id, v)} />
            </div>
            {on ? (
                <Slider
                    value={shown}
                    min={1}
                    max={50}
                    step={1}
                    unit="money"
                    testId="info-fee-amount"
                    onPreview={(v) => setShown(v)}
                    onCommit={(v) => {
                        setShown(v);
                        act.setGroupFee(id, v);
                    }}
                />
            ) : (
                <div className={s.facts}>{t("Placemaker.UI.Info.FeeOff", "Free: drivers park here without paying.")}</div>
            )}
        </div>
    );
};

/** "12 of 40 spaces taken" (parked cars on its stalls, as Parking Lot Tool's list); nothing until the stalls are counted. */
export const Occupancy = ({ spaces, taken }: { spaces?: number; taken?: number }) => {
    const { t } = useT();
    if (typeof spaces !== "number" || spaces <= 0) return null;
    return (
        <div className={s.occupancy} data-testid="info-occupancy">
            {t("Placemaker.UI.Info.Taken", "{0} of {1} spaces taken", [String(taken ?? 0), String(spaces)])}
        </div>
    );
};

export const CarParkRows = ({ id, fee, on, spaces, taken }: { id: number; fee: number; on: boolean; spaces?: number; taken?: number }) => (
    <>
        <Occupancy spaces={spaces} taken={taken} />
        <FeeRow id={id} fee={fee} on={on} />
    </>
);
