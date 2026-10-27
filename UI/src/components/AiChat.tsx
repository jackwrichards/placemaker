// AI builder (docs/AI-BUILDER.md): the AI category's design card. No types, no styles: a box for what the player wants,
// the conversation below it (the player, the AI, short step lines), Stop while it works, Start over. What the AI places
// shows in the sketch and the ghost like any type; Review and Build are the usual stages.

import React, { useEffect, useRef, useState } from "react";
import classNames from "classnames";
import { AiLine } from "../contract";
import { act, useAi } from "../lib/bindings";
import { useT } from "../lib/l10n";
import { Btn } from "./common";
import { ScrollArea } from "./ScrollArea";
import s from "./aiChat.module.scss";

const LineView = ({ line }: { line: AiLine }) => {
    const { t } = useT();
    if (line.who === "step")
        return (
            <div className={s.step} data-testid="ai-line-step">
                {t(line.key ?? "", line.key ?? "", line.args ?? [])}
            </div>
        );
    return (
        <div className={classNames(s.line, s[line.who])} data-testid={"ai-line-" + line.who}>
            {line.who === "error" ? t("Placemaker.Ai.Error", "Something went wrong: {0}", [line.text ?? ""]) : line.text}
        </div>
    );
};

export const AiChat = () => {
    const { t } = useT();
    const ai = useAi();
    const [text, setText] = useState("");
    const end = useRef<HTMLDivElement>(null);
    const count = ai?.lines.length ?? 0;
    useEffect(() => {
        try {
            end.current?.scrollIntoView();
        } catch {
            /* older views */
        }
    }, [count]);
    if (!ai) return null;
    const send = () => {
        if (!text.trim() || ai.busy) return;
        act.aiSend(text.trim());
        setText("");
    };
    const fresh = ai.lines.length === 0;
    return (
        <div className={s.card} data-testid="ai-chat">
            <div className={s.prompt}>
                <input
                    className={s.input}
                    data-testid="ai-input"
                    value={text}
                    placeholder={fresh ? t("Placemaker.Ai.Placeholder", "What do you want here?") : t("Placemaker.Ai.PlaceholderMore", "Tell it what to change")}
                    onChange={(e) => setText(e.target.value)}
                    onKeyDown={(e) => {
                        if (e.key === "Enter") send();
                        e.stopPropagation();
                    }}
                />
                <Btn variant="accent" label={t("Placemaker.Ai.Send", "Send")} disabled={ai.busy || !text.trim()} onClick={send} testId="ai-send" />
            </div>
            {fresh ? <div className={s.hint}>{t("Placemaker.Ai.Hint", "Describe what to build in your selection. The AI places every piece; you review it before anything is built.")}</div> : null}
            {count > 0 ? (
                <ScrollArea className={s.lines} outerClassName={s.linesOuter} testId="ai-lines">
                    {ai.lines.map((l, i) => (
                        <LineView key={i} line={l} />
                    ))}
                    <div ref={end} />
                </ScrollArea>
            ) : null}
            {ai.busy ? (
                <div className={s.working} data-testid="ai-working">
                    <span className={s.dot} />
                    <span className={s.workingText}>{t(ai.step || "Placemaker.Ai.Doing.Think", "", ai.stepArgs)}</span>
                    <Btn label={t("Placemaker.Ai.Stop", "Stop")} onClick={() => act.aiStop()} testId="ai-stop" />
                </div>
            ) : null}
            {!fresh ? (
                <div className={s.footer}>
                    <span className={s.meta} data-testid="ai-meta">
                        {t("Placemaker.Ai.Meta", "{0} pieces placed · ${1}", [ai.pieces, ai.cost.toFixed(2)])}
                    </span>
                    <Btn label={t("Placemaker.Ai.Reset", "Start over")} disabled={ai.busy} onClick={() => act.aiReset()} testId="ai-reset" />
                </div>
            ) : null}
        </div>
    );
};
