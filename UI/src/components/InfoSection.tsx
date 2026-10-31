// Section in the vanilla selected-info panel for anything built by Placemaker:
//   "Part of <name>" — Edit · Rename · Ungroup · Remove. (Phase 1: Edit is back; it opens Placemaker on the build, C#
//   PlacemakerToolSystem.EditGroup, so the next build can never replace it unseen as on 22 Oct.)
// Registered for the C# InfoSectionBase `Placemaker.Game.Groups.PlacemakerGroupSection` (the key is the
// full .NET class name: InfoSectionBase writes GetType().FullName as __Type — ParkingLotTool learned
// that the hard way). Props = what Game/Groups/PlacemakerGroupSection.OnWriteProperties writes:
// groupId, name, recipeNameKey, stats[], upkeep, memberCount, missingAssets, modified, removing.
// Unknown/missing fields degrade gracefully.
import React, { useState } from "react";
import { act } from "../lib/bindings";
import { humanize, useT } from "../lib/l10n";
import { formatValue, money } from "../lib/format";
import { ErrorBoundary } from "../lib/errorBoundary";
import { VC } from "../lib/vanilla";
import { RenameField } from "../stages/ManageStage";
import { Btn, Icon } from "./common";
import { CarParkRows } from "./FeeRow";
import s from "./infoSection.module.scss";

export const INFO_SECTION_TYPE = "Placemaker.Game.Groups.PlacemakerGroupSection";

type Props = {
    groupId?: number;
    id?: number;
    group?: number | string;
    name?: string;
    customName?: string;
    recipeName?: string | { key: string; args?: (string | number)[] };
    recipeNameKey?: string;
    canEdit?: boolean;
    stats?: { key: string; value: number; unit: string }[];
    upkeep?: number;
    memberCount?: number;
    missingAssets?: boolean;
    modified?: boolean;
    removing?: boolean;
    /** (P2-1) The car park's fee per park (-1 or absent = not a car park), and whether it is charged. */
    fee?: number;
    feeOn?: boolean;
    /** (P2-1) Its stalls and the parked cars on them (-1 = not counted / not a car park). */
    spaces?: number;
    taken?: number;
};

const Inner = (p: Props) => {
    const { t, tl } = useT();
    const [renaming, setRenaming] = useState(false);
    // Remove and Ungroup ask in the section itself (a dialog does not fit the game's info panel)
    const [asking, setAsking] = useState<null | "ungroup" | "remove">(null);
    const id = typeof p.groupId === "number" ? p.groupId : typeof p.id === "number" ? p.id : typeof p.group === "number" ? p.group : -1;
    const recipeKey = p.recipeNameKey || (typeof p.recipeName === "string" ? p.recipeName : "");
    const recipe = recipeKey ? t(recipeKey, "") : tl(typeof p.recipeName === "object" ? p.recipeName : undefined);
    const busy = !!p.removing;
    const stats = Array.isArray(p.stats) ? p.stats.filter((x) => x && x.unit === "count").slice(0, 3) : [];
    const name = p.customName || p.name || recipe || t("Placemaker.Name", "Placemaker");
    const content = (
        <div className={s.section} data-testid="info-section">
            <div className={s.head}>
                <Icon name="logo" size={18} color="#8fd1ff" />
                {renaming ? (
                    <RenameField id={id} name={p.customName ?? p.name ?? ""} onDone={() => setRenaming(false)} />
                ) : (
                    <div className={s.title}>
                        <span className={s.partOf}>{t("Placemaker.UI.Info.PartOf", "Part of")}</span> {name}
                    </div>
                )}
            </div>
            {p.customName || p.name ? <div className={s.kind}>{recipe}</div> : null}
            {stats.length || typeof p.upkeep === "number" ? (
                <div className={s.facts}>
                    {stats.map((x) => formatValue(x.value, "count") + " " + t(x.key, humanize(x.key).toLowerCase())).join(" · ")}
                    {typeof p.upkeep === "number" ? (stats.length ? " · " : "") + money(p.upkeep) + t("Placemaker.UI.PerMonth", "/mo") : ""}
                </div>
            ) : null}
            {typeof p.fee === "number" && p.fee >= 0 && id >= 0 ? <CarParkRows id={id} fee={p.fee} on={!!p.feeOn} spaces={p.spaces} taken={p.taken} /> : null}
            {p.missingAssets ? (
                <div className={s.note} data-testid="info-missing">
                    <Icon name="warning" size={14} color="#ffb547" />
                    <div className={s.noteText}>{t("Placemaker.UI.Info.Missing", "Some assets it used are missing (a mod or asset pack was removed). Edit to replace them.")}</div>
                </div>
            ) : null}
            {p.modified ? (
                <div className={s.note}>
                    <Icon name="info" size={14} color="#8fd1ff" />
                    <div className={s.noteText}>{t("Placemaker.UI.Info.Modified", "Changed with other tools since it was built. Editing rebuilds it from its design.")}</div>
                </div>
            ) : null}
            {busy ? <div className={s.facts}>{t("Placemaker.UI.Info.Removing", "Removing…")}</div> : null}
            {!renaming && !busy && asking ? (
                <div className={s.ask} data-testid="info-confirm" data-asking={asking}>
                    <div className={s.askText}>
                        {asking === "ungroup"
                            ? t("Placemaker.UI.Info.UngroupAsk", "Ungroup it for good? Its pieces become separate, as if you placed them yourself. This can't be undone.")
                            : t("Placemaker.UI.Info.RemoveAsk", "Remove all of it? Buildings that grew next to it stay.")}
                    </div>
                    <div className={s.actions}>
                        <Btn
                            variant={asking === "remove" ? "danger" : "accent"}
                            icon={asking === "remove" ? "trash" : "ungroup"}
                            iconSize={14}
                            label={asking === "remove" ? t("Placemaker.UI.Manage.RemoveConfirm", "Remove") : t("Placemaker.UI.Manage.UngroupConfirm", "Ungroup")}
                            onClick={() => {
                                const what = asking;
                                setAsking(null);
                                if (what === "remove") act.removeGroup(id);
                                else act.ungroupGroup(id);
                            }}
                            testId="info-confirm-ok"
                        />
                        <Btn variant="solid" label={t("Placemaker.UI.Cancel", "Cancel")} onClick={() => setAsking(null)} testId="info-confirm-cancel" />
                    </div>
                </div>
            ) : null}
            {!renaming && !busy && !asking ? (
                <div className={s.actions}>
                    <Btn
                        variant="accent"
                        icon="draw"
                        iconSize={14}
                        label={t("Placemaker.UI.Manage.Edit", "Edit")}
                        description={t("Placemaker.UI.Info.EditTip", "Opens Placemaker on it: change the design, then build again to replace it.")}
                        disabled={id < 0 || p.canEdit === false}
                        onClick={() => act.editGroup(id)}
                        testId="info-edit"
                    />
                    <Btn variant="solid" icon="edit" iconSize={14} label={t("Placemaker.UI.Manage.Rename", "Rename")} disabled={id < 0} onClick={() => setRenaming(true)} testId="info-rename" />
                    <Btn variant="solid" icon="ungroup" iconSize={14} label={t("Placemaker.UI.Manage.Ungroup", "Ungroup")} description={t("Placemaker.UI.Manage.UngroupTip", "Make its pieces separate for good, as if you placed them yourself. This can't be undone.")} disabled={id < 0} onClick={() => setAsking("ungroup")} testId="info-ungroup" />
                    <div className={s.flex} />
                    <Btn variant="danger" icon="trash" iconSize={14} label={t("Placemaker.UI.Manage.Remove", "Remove")} disabled={id < 0} onClick={() => setAsking("remove")} testId="info-remove" />
                </div>
            ) : null}
        </div>
    );
    return VC.InfoSection ? <VC.InfoSection disableFocus={true}>{content}</VC.InfoSection> : content;
};

export const PlacemakerInfoSection = (p: Props) => (
    <ErrorBoundary name="info-section" fallback={null}>
        <Inner {...p} />
    </ErrorBoundary>
);
