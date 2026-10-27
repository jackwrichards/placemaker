// AI builder (docs/AI-BUILDER.md): a warning before anything leaves the AI chat (the user, 27 Oct: "we need a warning
// for leaving/clicking anything that makes it leave this chat"). While a conversation or a placed design exists, the
// actions that leave it (another type, clearing the area, closing Placemaker, back to the area, opening a built group)
// wait for the player's yes; leaving then forgets the conversation and what the AI placed, as the dialog says.

import { useEffect, useState } from "react";

let active = false;
let pending: (() => void) | null = null;
const listeners = new Set<() => void>();
const notify = () => listeners.forEach((l) => l());

/** The chat sets this while it has something to lose. */
export function setAiLeaveGuard(on: boolean) {
    active = on;
}

/** Runs the action now, or after the player confirms leaving the AI chat. */
export function guardAiLeave(go: () => void) {
    if (!active) return go();
    pending = go;
    notify();
}

/** The dialog's state: is a leave waiting, and its two answers. */
export function useAiLeave(): { asking: boolean; leave: (reset: () => void) => void; stay: () => void } {
    const [, bump] = useState(0);
    useEffect(() => {
        const l = () => bump((n) => n + 1);
        listeners.add(l);
        return () => {
            listeners.delete(l);
        };
    }, []);
    return {
        asking: pending != null,
        leave: (reset) => {
            const go = pending;
            pending = null;
            active = false;
            notify();
            reset();
            go?.();
        },
        stay: () => {
            pending = null;
            notify();
        },
    };
}
