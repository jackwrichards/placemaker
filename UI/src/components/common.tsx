// Small building blocks shared by every stage. Plain elements (not vanilla Button) so they behave the
// same in the game and the browser mock; vanilla sounds are played through the audio binding.
import React, { ReactNode, useState } from "react";
import classNames from "classnames";
import { bundledName, resolveIcon, Tint, tintFor } from "../lib/icons";
import { playSound } from "../lib/bindings";
import { Tip } from "../lib/tips";
import { useOpen } from "../lib/collapse";
import s from "./common.module.scss";

export const Icon = ({
    name,
    size = 18,
    color,
    tint,
    className,
    fallback,
}: {
    name?: string;
    size?: number;
    /** Colour hint, mapped to the nearest baked tint (see lib/icons tintFor). */
    color?: string;
    tint?: Tint;
    className?: string;
    fallback?: string;
}) => {
    const [failed, setFailed] = useState<string | null>(null);
    const tn = tint ?? tintFor(color);
    const r0 = resolveIcon(name, fallback, tn);
    // An external (game) icon that fails to load falls back to our glyph.
    const r = failed === r0.src ? resolveIcon(fallback ?? "layers", undefined, tn) : r0;
    const style: React.CSSProperties = { width: size + "rem", height: size + "rem" };
    if (!r.src) return <div className={classNames(s.icon, className)} style={style} />;
    return (
        <img
            className={classNames(s.icon, className)}
            style={style}
            src={r.src}
            data-icon={r.bundled ? bundledName(name ?? "") ?? fallback ?? "layers" : "external"}
            onError={() => r === r0 && setFailed(r0.src)}
        />
    );
};

type BtnProps = {
    icon?: string;
    label?: ReactNode;
    title?: ReactNode;
    description?: ReactNode;
    onClick?: () => void;
    disabled?: boolean;
    selected?: boolean;
    className?: string;
    iconColor?: string;
    iconSize?: number;
    variant?: "ghost" | "solid" | "accent" | "danger";
    testId?: string;
    children?: ReactNode;
    tipDirection?: "up" | "down" | "left" | "right";
};

export const Btn = ({
    icon, label, title, description, onClick, disabled, selected, className, iconColor, iconSize = 15,
    variant = "ghost", testId, children, tipDirection,
}: BtnProps) => {
    const el = (
        <button
            type="button"
            data-testid={testId}
            aria-disabled={disabled ? "true" : "false"}
            aria-pressed={selected ? "true" : "false"}
            data-icon-button={!label && !children ? "true" : undefined}
            className={classNames(s.btn, s[variant], { [s.selected]: selected, [s.disabled]: disabled, [s.iconOnly]: !label && !children }, className)}
            onMouseEnter={() => !disabled && playSound("hover-item")}
            onClick={(e) => {
                e.stopPropagation();
                if (disabled || !onClick) return;
                playSound("select-item");
                onClick();
            }}
        >
            {icon ? <Icon name={icon} size={iconSize} color={iconColor} tint={iconColor ? undefined : variant === "accent" && !disabled ? "dark" : undefined} /> : null}
            {label ? <div className={s.btnLabel}>{label}</div> : null}
            {children}
        </button>
    );
    return (
        <Tip title={title} description={description} direction={tipDirection}>
            {el}
        </Tip>
    );
};

export interface SegOption {
    value: string;
    label?: ReactNode;
    icon?: string;
    title?: ReactNode;
    description?: ReactNode;
    color?: string;
}

export const Segmented = ({
    options,
    value,
    onChange,
    iconOnly,
    testId,
    stretch,
}: {
    options: SegOption[];
    value: string;
    onChange: (v: string) => void;
    iconOnly?: boolean;
    testId?: string;
    stretch?: boolean;
}) => (
    <div className={classNames(s.segmented, { [s.stretch]: stretch })} data-testid={testId}>
        {options.map((o) => (
            // A long label (a translation) may end in "…": then its whole text on hover.
            <Tip key={o.value} title={o.title ?? (iconOnly || (typeof o.label === "string" && o.label.length > 14) ? o.label : undefined)} description={o.description}>
                <button
                    type="button"
                    data-testid={testId ? `${testId}-${o.value}` : undefined}
                    aria-pressed={o.value === value ? "true" : "false"}
                    data-icon-button={iconOnly || !o.label ? "true" : undefined}
                    className={classNames(s.seg, { [s.segOn]: o.value === value, [s.segIconOnly]: iconOnly || !o.label })}
                    onMouseEnter={() => playSound("hover-item")}
                    onClick={(e) => {
                        e.stopPropagation();
                        if (o.value !== value) {
                            playSound("select-item");
                            onChange(o.value);
                        }
                    }}
                >
                    {o.icon ? <Icon name={o.icon} size={14} tint={o.value === value ? "light" : "dim"} /> : null}
                    {!iconOnly && o.label ? <div className={s.segLabel}>{o.label}</div> : null}
                </button>
            </Tip>
        ))}
    </div>
);

export const Keys = ({ keys }: { keys: string[] }) => (
    <div className={s.keys}>
        {keys.map((k, i) => (
            <div key={i} className={s.key}>
                {k}
            </div>
        ))}
    </div>
);

export const Check = ({
    checked,
    onChange,
    label,
    testId,
    disabled,
}: {
    checked: boolean;
    onChange: (v: boolean) => void;
    label?: ReactNode;
    testId?: string;
    disabled?: boolean;
}) => (
    <button
        type="button"
        role="checkbox"
        aria-checked={checked ? "true" : "false"}
        data-testid={testId}
        className={classNames(s.check, { [s.checkOn]: checked, [s.disabled]: disabled })}
        onClick={(e) => {
            e.stopPropagation();
            if (disabled) return;
            playSound(checked ? "toggle-off" : "toggle-on");
            onChange(!checked);
        }}
    >
        <div className={s.checkBox}>{checked ? <Icon name="check" size={12} tint="dark" /> : null}</div>
        {label ? <div className={s.checkLabel}>{label}</div> : null}
    </button>
);

export const Switch = ({ on, onChange, testId }: { on: boolean; onChange: (v: boolean) => void; testId?: string }) => (
    <button
        type="button"
        role="switch"
        aria-checked={on ? "true" : "false"}
        data-testid={testId}
        className={classNames(s.switch, { [s.switchOn]: on })}
        onClick={(e) => {
            e.stopPropagation();
            playSound(on ? "toggle-off" : "toggle-on");
            onChange(!on);
        }}
    >
        <div className={s.knob} />
    </button>
);

/** A titled block of the panel body: small caps heading with an optional icon, count and right-side actions. */
export const Section = ({
    title,
    icon,
    count,
    right,
    children,
    testId,
    className,
}: {
    title: ReactNode;
    icon?: string;
    count?: ReactNode;
    right?: ReactNode;
    children?: ReactNode;
    testId?: string;
    className?: string;
}) => (
    <div className={classNames(s.section, className)} data-testid={testId}>
        <div className={s.sectionHead}>
            {icon ? <Icon name={icon} size={13} tint="dim" className={s.sectionIcon} /> : null}
            <div className={s.sectionTitle}>{title}</div>
            {count !== undefined && count !== null ? <div className={s.sectionCount}>{count}</div> : null}
            <div className={s.sectionRule} />
            {right}
        </div>
        {children}
    </div>
);

/**
 * A foldable section: one 24rem header line (chevron · icon · title · summary when folded · actions).
 * Open/closed is remembered per `id` (lib/collapse). `summary` is what the player reads instead of
 * unfolding ("90° · 6.0 m"), so it should hold the key values.
 */
export const Collapsible = ({
    id,
    title,
    icon,
    summary,
    right,
    defaultOpen = false,
    children,
    testId,
    nested,
    className,
}: {
    id: string;
    title: ReactNode;
    icon?: string;
    summary?: ReactNode;
    right?: ReactNode;
    defaultOpen?: boolean;
    children?: ReactNode;
    testId?: string;
    nested?: boolean;
    className?: string;
}) => {
    const [open, setOpen] = useOpen(id, defaultOpen);
    return (
        <div className={classNames(s.fold, { [s.foldNested]: nested }, className)} data-testid={testId} data-open={open ? "true" : "false"}>
            <div className={s.foldHead}>
                <button
                    type="button"
                    aria-expanded={open ? "true" : "false"}
                    data-testid={testId ? testId + "-toggle" : undefined}
                    className={s.foldToggle}
                    onClick={(e) => {
                        e.stopPropagation();
                        playSound(open ? "toggle-off" : "toggle-on");
                        setOpen(!open);
                    }}
                >
                    <Icon name={open ? "chevronDown" : "chevronRight"} size={12} tint="dim" />
                    {icon ? <Icon name={icon} size={14} tint="dim" className={s.foldIcon} /> : null}
                    <div className={s.foldTitle}>{title}</div>
                    {!open && summary ? (
                        <div className={s.foldSummary} data-ellipsis data-testid={testId ? testId + "-summary" : undefined}>
                            {summary}
                        </div>
                    ) : (
                        <div className={s.foldSpacer} />
                    )}
                </button>
                {right ? <div className={s.foldRight}>{right}</div> : null}
            </div>
            {open ? <div className={s.foldBody}>{children}</div> : null}
        </div>
    );
};

export const Dot =({ color }: { color: string }) => <div className={s.dot} style={{ backgroundColor: color }} />;
