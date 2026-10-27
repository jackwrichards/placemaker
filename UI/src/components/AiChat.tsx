// AI builder (docs/AI-BUILDER.md): the AI category's design card, rebuilt after the user's D- (27 Oct: no spinner,
// overlapping, the AI's tool use unclear). Top to bottom:
//   * a header: what it is and which model works;
//   * before the first message: a welcome with example requests and the mods switch (fixed once it starts);
//   * the conversation: the player's and the AI's messages as bubbles, the AI's actions grouped by its step (the latest
//     open, older ones folded into one line with their icons), the phases (review, details) as dividers;
//   * while it works: the phases as a progress row, a spinner with what it does now, the time, and Stop;
//   * the message box at the bottom, as in any chat;
//   * a footer: pieces, cost, the asset rule, Start over (asked first).
// AiLeaveGuard (also here) warns before anything leaves the chat.

import React, { useEffect, useMemo, useRef, useState } from "react";
import classNames from "classnames";
import { AiLine, AiView } from "../contract";
import { act, useAi, useDesign } from "../lib/bindings";
import { setAiLeaveGuard, useAiLeave } from "../lib/aiLeave";
import { useT } from "../lib/l10n";
import { Btn, Icon, Switch } from "./common";
import { Confirm } from "./dialog";
import { ScrollArea } from "./ScrollArea";
import s from "./aiChat.module.scss";

/** The icon of each of the AI's tools. */
const TOOL_ICON: Record<string, string> = {
    place_objects: "cube",
    place_along: "more",
    scatter_objects: "tree",
    place_path: "path",
    shape_ground: "mountain",
    paint_ground: "paint",
    place_surface: "plaza",
    search_assets: "search",
    asset_groups: "layers",
    look_at_assets: "eye",
    remove: "trash",
    check: "check",
    list_placed: "board",
    view_map: "compass",
    wish: "star",
};

const PHASES = ["read", "build", "review", "detail", "done"] as const;

type Item =
    | { kind: "msg"; line: AiLine; i: number }
    | { kind: "round"; round: number; tools: AiLine[]; i: number }
    | { kind: "phase"; line: AiLine; i: number };

/** The lines as the chat shows them: messages, one block per AI step, phase dividers. */
function group(lines: AiLine[]): Item[] {
    const out: Item[] = [];
    lines.forEach((l, i) => {
        if (l.who === "tool") {
            const last = out[out.length - 1];
            if (last && last.kind === "round" && last.round === (l.round ?? 0)) last.tools.push(l);
            else out.push({ kind: "round", round: l.round ?? 0, tools: [l], i });
        } else if (l.who === "phase") out.push({ kind: "phase", line: l, i });
        else out.push({ kind: "msg", line: l, i });
    });
    return out;
}

/** "anthropic/claude-sonnet-5.5" → "Sonnet 5.5". */
function modelName(id: string): string {
    const last = (id || "").split("/").pop() ?? "";
    const m = last.match(/(opus|sonnet|haiku|fable)[-_ ]?([\d.]+)?/i);
    if (m) return m[1].charAt(0).toUpperCase() + m[1].slice(1).toLowerCase() + (m[2] ? " " + m[2] : "");
    return last;
}

function clock(ms: number): string {
    const sec = Math.max(0, Math.floor(ms / 1000));
    const m = Math.floor(sec / 60);
    return m > 0 ? `${m}:${String(sec % 60).padStart(2, "0")}` : `${sec} s`;
}

const ToolRow = ({ line }: { line: AiLine }) => {
    const { t } = useT();
    const problems = line.problems ?? 0;
    const refused = line.key === "Placemaker.Ai.Tool.Refused";
    return (
        <div className={classNames(s.tool, { [s.toolRefused]: refused })} data-testid="ai-tool">
            <Icon name={refused ? "warning" : TOOL_ICON[line.tool ?? ""] ?? "sparkle"} size={13} tint={refused ? "warn" : "dim"} className={s.toolIcon} />
            <div className={s.toolText}>{t(line.key ?? "", line.key ?? "", line.args ?? [])}</div>
            {problems > 0 && !refused ? (
                <div className={s.badge} data-testid="ai-tool-problems">
                    {t("Placemaker.Ai.Problems", "{0} to fix", [problems])}
                </div>
            ) : null}
        </div>
    );
};

const RoundBlock = ({ round, tools, open, onToggle, live }: { round: number; tools: AiLine[]; open: boolean; onToggle: () => void; live: boolean }) => {
    const { t } = useT();
    const problems = tools.reduce((n, l) => n + (l.problems ?? 0), 0);
    const icons = Array.from(new Set(tools.map((l) => TOOL_ICON[l.tool ?? ""] ?? "sparkle"))).slice(0, 6);
    return (
        <div className={classNames(s.round, { [s.roundLive]: live })} data-testid="ai-round">
            <div className={s.roundHead} onClick={onToggle}>
                <Icon name={open ? "chevronDown" : "chevronRight"} size={11} tint="dim" className={s.roundChevron} />
                <div className={s.roundTitle}>{t("Placemaker.Ai.Round", "Step {0}", [round])}</div>
                <div className={s.roundCount}>{t("Placemaker.Ai.Actions", "{0} actions", [tools.length])}</div>
                {!open ? (
                    <div className={s.roundIcons}>
                        {icons.map((n) => (
                            <Icon key={n} name={n} size={11} tint="dim" className={s.roundIcon} />
                        ))}
                    </div>
                ) : null}
                <div className={s.flex} />
                {problems > 0 ? <div className={s.badge}>{t("Placemaker.Ai.Problems", "{0} to fix", [problems])}</div> : null}
            </div>
            {open ? (
                <div className={s.roundBody}>
                    {tools.map((l, k) => (
                        <ToolRow key={k} line={l} />
                    ))}
                </div>
            ) : null}
        </div>
    );
};

const Bubble = ({ line }: { line: AiLine }) => {
    const { t } = useT();
    if (line.who === "step")
        return <div className={s.note}>{t(line.key ?? "", line.key ?? "", line.args ?? [])}</div>;
    if (line.who === "you")
        return (
            <div className={classNames(s.bubble, s.you)} data-testid="ai-line-you">
                {line.text}
            </div>
        );
    if (line.who === "error")
        return (
            <div className={classNames(s.bubble, s.error)} data-testid="ai-line-error">
                <Icon name="error" size={13} tint="danger" className={s.bubbleIcon} />
                <div className={s.bubbleText}>{t("Placemaker.Ai.Error", "Something went wrong: {0}", [line.text ?? ""])}</div>
            </div>
        );
    return (
        <div className={classNames(s.bubble, s.ai)} data-testid="ai-line-ai">
            <Icon name="sparkle" size={13} tint="accent" className={s.bubbleIcon} />
            <div className={s.bubbleText}>{line.text}</div>
        </div>
    );
};

/** The phases as a row of steps, the current one lit, while the AI works. */
const Progress = ({ ai }: { ai: AiView }) => {
    const { t } = useT();
    const [now, setNow] = useState(Date.now());
    useEffect(() => {
        const id = setInterval(() => setNow(Date.now()), 1000);
        return () => clearInterval(id);
    }, []);
    const phase = ai.phase === "fix" ? "detail" : ai.phase || "read";
    const at = PHASES.indexOf(phase as (typeof PHASES)[number]);
    return (
        <div className={s.progress} data-testid="ai-working">
            <div className={s.phases}>
                {PHASES.slice(0, 4).map((p, k) => (
                    <React.Fragment key={p}>
                        {k > 0 ? <div className={classNames(s.phaseLine, { [s.phaseLineDone]: k <= at })} /> : null}
                        <div className={classNames(s.phase, { [s.phaseDone]: k < at, [s.phaseNow]: k === at })} data-testid={"ai-phase-" + p}>
                            <div className={s.phaseDot} />
                            <div className={s.phaseLabel}>{t("Placemaker.Ai.PhaseName." + p, p)}</div>
                        </div>
                    </React.Fragment>
                ))}
            </div>
            <div className={s.now}>
                <div className={s.spinner} data-testid="ai-spinner" />
                <div className={s.nowText}>{t(ai.step || "Placemaker.Ai.Doing.Think", "", ai.stepArgs)}</div>
                <div className={s.nowTime}>{ai.startedAt > 0 ? clock(now - ai.startedAt) : ""}</div>
                <Btn icon="close" label={t("Placemaker.Ai.Stop", "Stop")} onClick={() => act.aiStop()} testId="ai-stop" />
            </div>
        </div>
    );
};

const Welcome = ({ ai, onPick }: { ai: AiView; onPick: (text: string) => void }) => {
    const { t } = useT();
    const examples = [1, 2, 3, 4].map((n) => t("Placemaker.Ai.Example." + n, ""));
    return (
        <div className={s.welcome} data-testid="ai-welcome">
            <div className={s.welcomeTitle}>{t("Placemaker.Ai.WelcomeTitle", "What should it build here?")}</div>
            <div className={s.welcomeText}>{t("Placemaker.Ai.Hint", "Describe what to build in your selection. The AI places every piece; you review it before anything is built.")}</div>
            <div className={s.examples}>
                {examples
                    .filter((e) => e)
                    .map((e, k) => (
                        <div key={k} className={s.example} onClick={() => onPick(e)} data-testid="ai-example">
                            {e}
                        </div>
                    ))}
            </div>
            <div className={s.modsRow}>
                <Switch on={ai.mods} onChange={(v) => act.aiMods(v)} testId="ai-mods" />
                <div className={s.modsText}>
                    <div className={s.modsTitle}>{t("Placemaker.Ai.Mods", "Use modded assets")}</div>
                    <div className={s.modsHint}>{t("Placemaker.Ai.ModsHint", "Off: the base game's assets only. Fixed once you send your first message.")}</div>
                </div>
            </div>
        </div>
    );
};

/** Warns before anything leaves the AI chat while there is something to lose (lib/aiLeave). */
export const AiLeaveGuard = () => {
    const { t } = useT();
    const ai = useAi();
    const design = useDesign();
    const on = !!ai && design.root?.layer === "ai" && (ai.lines.length > 0 || ai.pieces > 0);
    useEffect(() => {
        setAiLeaveGuard(on);
        return () => setAiLeaveGuard(false);
    }, [on]);
    const leave = useAiLeave();
    if (!leave.asking) return null;
    return (
        <Confirm
            title={t("Placemaker.Ai.LeaveTitle", "Leave the AI builder?")}
            message={t("Placemaker.Ai.LeaveText", "The conversation and everything the AI placed will be lost. What is already built stays.")}
            confirm={t("Placemaker.Ai.Leave", "Leave")}
            danger
            onConfirm={() => leave.leave(() => act.aiReset())}
            onCancel={leave.stay}
        />
    );
};

export const AiChat = () => {
    const { t } = useT();
    const ai = useAi();
    const [text, setText] = useState("");
    const [opened, setOpened] = useState<Record<number, boolean>>({});
    const [askReset, setAskReset] = useState(false);
    const end = useRef<HTMLDivElement>(null);
    const count = ai?.lines.length ?? 0;
    const items = useMemo(() => group(ai?.lines ?? []), [ai?.lines]);
    useEffect(() => {
        // the conversation box itself to its end (scrollIntoView moved the panel instead, and left the AI's last words hidden)
        const box = end.current?.parentElement;
        if (box) box.scrollTop = box.scrollHeight;
    }, [count, ai?.busy]);
    if (!ai) return null;
    const send = () => {
        if (!text.trim() || ai.busy) return;
        act.aiSend(text.trim());
        setText("");
    };
    const fresh = ai.lines.length === 0;
    const lastRound = items.reduce((r, it) => (it.kind === "round" ? Math.max(r, it.round) : r), 0);
    return (
        <div className={s.card} data-testid="ai-chat">
            <div className={s.header}>
                <Icon name="sparkle" size={15} tint="accent" />
                <div className={s.headerTitle}>{t("Placemaker.Ai.Name", "AI builder")}</div>
                <div className={s.flex} />
                {ai.model ? (
                    <div className={s.chip} data-testid="ai-model">
                        {modelName(ai.model)}
                        {ai.thinking ? " · " + t("Placemaker.Settings.AiThinking." + ai.thinking.charAt(0).toUpperCase() + ai.thinking.slice(1), ai.thinking) : ""}
                    </div>
                ) : null}
            </div>

            {fresh && !ai.busy ? <Welcome ai={ai} onPick={setText} /> : null}

            {count > 0 ? (
                <ScrollArea className={s.lines} outerClassName={s.linesOuter} testId="ai-lines">
                    {items.map((it) =>
                        it.kind === "round" ? (
                            <RoundBlock
                                key={"r" + it.i}
                                round={it.round}
                                tools={it.tools}
                                live={ai.busy && it.round === lastRound}
                                open={opened[it.round] ?? it.round === lastRound}
                                onToggle={() => setOpened((o) => ({ ...o, [it.round]: !(o[it.round] ?? it.round === lastRound) }))}
                            />
                        ) : it.kind === "phase" ? (
                            <div key={"p" + it.i} className={s.divider} data-testid="ai-phase-line">
                                <div className={s.dividerLine} />
                                <div className={s.dividerText}>{t(it.line.key ?? "", it.line.key ?? "")}</div>
                                <div className={s.dividerLine} />
                            </div>
                        ) : (
                            <Bubble key={"m" + it.i} line={it.line} />
                        ),
                    )}
                    <div ref={end} />
                </ScrollArea>
            ) : null}

            {ai.busy ? <Progress ai={ai} /> : null}

            <div className={classNames(s.composer, { [s.composerBusy]: ai.busy })}>
                <input
                    className={s.input}
                    data-testid="ai-input"
                    value={text}
                    disabled={ai.busy}
                    placeholder={
                        ai.busy
                            ? t("Placemaker.Ai.PlaceholderBusy", "Working… you can write when it is done")
                            : fresh
                              ? t("Placemaker.Ai.Placeholder", "What do you want here?")
                              : t("Placemaker.Ai.PlaceholderMore", "Tell it what to change")
                    }
                    onChange={(e) => setText(e.target.value)}
                    onKeyDown={(e) => {
                        if (e.key === "Enter") send();
                        e.stopPropagation();
                    }}
                />
                <Btn variant="accent" label={t("Placemaker.Ai.Send", "Send")} disabled={ai.busy || !text.trim()} onClick={send} testId="ai-send" />
            </div>

            {!fresh ? (
                <div className={s.footer}>
                    <div className={s.meta} data-testid="ai-meta">
                        {t("Placemaker.Ai.Meta", "{0} pieces placed · ${1}", [ai.pieces, ai.cost.toFixed(2)])}
                    </div>
                    <div className={s.lockChip} data-testid="ai-mods-locked">
                        <Icon name="lock" size={11} tint="dim" className={s.lockIcon} />
                        {ai.mods ? t("Placemaker.Ai.ModsOn", "Modded assets") : t("Placemaker.Ai.ModsOff", "Base game assets")}
                    </div>
                    <div className={s.flex} />
                    <Btn icon="reset" label={t("Placemaker.Ai.Reset", "Start over")} disabled={ai.busy} onClick={() => setAskReset(true)} testId="ai-reset" />
                </div>
            ) : null}

            {askReset ? (
                <Confirm
                    title={t("Placemaker.Ai.ResetTitle", "Start over?")}
                    message={t("Placemaker.Ai.ResetText", "The conversation and everything the AI placed will be lost.")}
                    confirm={t("Placemaker.Ai.Reset", "Start over")}
                    danger
                    onConfirm={() => {
                        setAskReset(false);
                        act.aiReset();
                    }}
                    onCancel={() => setAskReset(false)}
                />
            ) : null}
        </div>
    );
};
