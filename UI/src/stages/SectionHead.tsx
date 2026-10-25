// The Design page reads as one story, top to bottom (user 2026-10-20: "the whole font should match ... it should tell a
// story"): Pick what to make (and edit) → Add entrances → Shape (or Zoning) → Extras. Every section opens with this one
// header line, the same everywhere: a round icon badge · TITLE · count · a short note · hairline rule · its buttons.
import React, { ReactNode } from "react";
import classNames from "classnames";
import { Icon } from "../components/common";
import { Tip } from "../lib/tips";
import s from "./sectionHead.module.scss";

export const SectionHead = ({
    icon,
    title,
    tip,
    count,
    sub,
    tone,
    fill,
    right,
    first,
    wrap,
    testId,
}: {
    icon: string;
    title: string;
    /** Tooltip text for the title. */
    tip?: string;
    /** A small count pill after the title (hidden when 0 or undefined). */
    count?: number;
    /** The short note after the title: what it is ("how it's built") or where it stands ("Add one to build"). */
    sub?: ReactNode;
    /** "warn": the section needs something before the build (badge and note in the warning colour). */
    tone?: "warn";
    /** Replaces the hairline rule (e.g. the search box). */
    fill?: ReactNode;
    /** Buttons at the end of the line. */
    right?: ReactNode;
    /** The page's first section: no space above it. */
    first?: boolean;
    /** The buttons take the next line when they do not fit beside the title. */
    wrap?: boolean;
    testId?: string;
}) => {
    const warn = tone === "warn";
    const label = <div className={s.title}>{title}</div>;
    return (
        <div className={classNames(s.head, { [s.headFirst]: first, [s.headWrap]: wrap })} data-testid={testId} data-tone={tone ?? ""}>
            <div className={classNames(s.badge, { [s.badgeWarn]: warn })}>
                <Icon name={icon} size={11} tint={warn ? "warn" : "accent"} />
            </div>
            {tip ? (
                <Tip title={title} description={tip}>
                    {label}
                </Tip>
            ) : (
                label
            )}
            {count ? <div className={s.count}>{count}</div> : null}
            {sub === undefined || sub === null || sub === "" ? null : typeof sub === "string" ? (
                <div className={classNames(s.sub, { [s.subWarn]: warn })} data-ellipsis>
                    {sub}
                </div>
            ) : (
                <div className={s.subBox}>{sub}</div>
            )}
            {fill ?? <div className={s.rule} />}
            {right ? <div className={s.right}>{right}</div> : null}
        </div>
    );
};
