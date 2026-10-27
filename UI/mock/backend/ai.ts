// The AI builder's chat for the mock (no real model): a scripted run that plays out a few steps of tool use, the review
// and the detail pass, so the chat card (src/components/AiChat.tsx) can be seen and tested. ?aiStep=ms sets the pace.
import type { AiLine, AiView } from "../../src/contract";

type Push = () => void;

const SCRIPT: { phase?: AiView["phase"]; step: string; stepArgs?: (string | number)[]; tools: AiLine[] }[] = [
    { phase: "build", step: "Placemaker.Ai.Doing.Shape", tools: [
        { who: "tool", tool: "shape_ground", key: "Placemaker.Ai.Tool.shape_ground", args: [] },
        { who: "tool", tool: "shape_ground", key: "Placemaker.Ai.Tool.shape_ground", args: [] },
        { who: "tool", tool: "paint_ground", key: "Placemaker.Ai.Tool.paint_ground", args: [4210] },
        { who: "tool", tool: "place_surface", key: "Placemaker.Ai.Tool.place_surface", args: [380] },
    ] },
    { step: "Placemaker.Ai.Doing.Path", tools: [
        { who: "tool", tool: "place_path", key: "Placemaker.Ai.Tool.place_path", args: [142], problems: 1 },
        { who: "tool", tool: "place_path", key: "Placemaker.Ai.Tool.place_path", args: [64] },
        { who: "tool", tool: "search_assets", key: "Placemaker.Ai.Tool.search_assets", args: ["willow"] },
    ] },
    { step: "Placemaker.Ai.Doing.Place", stepArgs: [60], tools: [
        { who: "tool", tool: "scatter_objects", key: "Placemaker.Ai.Tool.scatter_objects", args: [48] },
        { who: "tool", tool: "place_along", key: "Placemaker.Ai.Tool.place_along", args: [14] },
        { who: "tool", tool: "place_objects", key: "Placemaker.Ai.Tool.place_objects", args: [22], problems: 2 },
        { who: "tool", tool: "place_objects", key: "Placemaker.Ai.Tool.Refused", args: [] },
    ] },
    { phase: "review", step: "Placemaker.Ai.Doing.Map", tools: [
        { who: "tool", tool: "remove", key: "Placemaker.Ai.Tool.remove", args: [2] },
        { who: "tool", tool: "place_objects", key: "Placemaker.Ai.Tool.place_objects", args: [18] },
        { who: "tool", tool: "check", key: "Placemaker.Ai.Tool.checkClean", args: [0] },
    ] },
    { phase: "detail", step: "Placemaker.Ai.Doing.Place", stepArgs: [40], tools: [
        { who: "tool", tool: "place_along", key: "Placemaker.Ai.Tool.place_along", args: [12] },
        { who: "tool", tool: "scatter_objects", key: "Placemaker.Ai.Tool.scatter_objects", args: [36] },
    ] },
];

const NOTE = "A neighbourhood park around a pond, with a loop path, a playground, groves and a lawn for picnics. Benches face the water and lamps line the paths.";

export class MockAi {
    view: AiView = { status: "idle", busy: false, step: "", stepArgs: [], cost: 0, tokens: 0, pieces: 0, phase: "", round: 0, startedAt: 0, model: "anthropic/claude-sonnet-5.5", thinking: "medium", mods: false, modsLocked: false, lines: [] };
    private timer: ReturnType<typeof setTimeout> | null = null;
    private pace = Number(new URLSearchParams(typeof location !== "undefined" ? location.search : "").get("aiStep")) || 700;

    constructor(private push: Push) {}

    trigger(name: string, a: unknown[]): boolean {
        switch (name) {
            case "aiSend":
                this.send(String(a[0] ?? ""));
                return true;
            case "aiStop":
                this.end("stopped", { who: "step", key: "Placemaker.Ai.Line.Stopped" });
                return true;
            case "aiReset":
                if (this.timer) clearTimeout(this.timer);
                this.view = { ...this.view, status: "idle", busy: false, step: "", stepArgs: [], cost: 0, tokens: 0, pieces: 0, phase: "", round: 0, startedAt: 0, modsLocked: false, lines: [] };
                return true;
            case "aiMods":
                if (!this.view.modsLocked) this.view = { ...this.view, mods: !!a[0] };
                return true;
        }
        return false;
    }

    private send(text: string) {
        if (!text.trim() || this.view.busy) return;
        const v = this.view;
        const vision = { who: "vision" as const, text: ["Willow Hollow", "A sunken garden around a reed-fringed pond, with a lookout hill and a hidden play glade.", "A timber boardwalk that zig-zags over the reeds to a gazebo on an island", "A birch grove with hammocks on the north rise", "A cherry avenue from the main gate to the water", "A sand play glade behind a willow screen"].join("\n") };
        this.view = { ...v, status: "working", busy: true, modsLocked: true, phase: "read", step: "Placemaker.Ai.Doing.Read", stepArgs: [], startedAt: Date.now(), lines: [...v.lines, { who: "you", text }, vision] };
        let k = 0;
        const tick = () => {
            if (k >= SCRIPT.length) {
                this.view = { ...this.view, phase: "done" };
                this.end("done", { who: "ai", text: NOTE });
                return;
            }
            const s = SCRIPT[k++];
            const round = this.view.round + 1;
            const lines = [...this.view.lines];
            if (s.phase === "review") lines.push({ who: "phase", key: "Placemaker.Ai.Phase.Review" });
            if (s.phase === "detail") lines.push({ who: "phase", key: "Placemaker.Ai.Phase.Detail" });
            for (const l of s.tools) lines.push({ ...l, round });
            this.view = {
                ...this.view, round, lines, phase: s.phase ?? this.view.phase, step: s.step, stepArgs: s.stepArgs ?? [],
                pieces: this.view.pieces + 30, cost: this.view.cost + 0.031, tokens: this.view.tokens + 41000,
            };
            this.push();
            this.timer = setTimeout(tick, this.pace);
        };
        this.timer = setTimeout(tick, this.pace);
    }

    private end(status: AiView["status"], line: AiLine) {
        if (this.timer) clearTimeout(this.timer);
        this.timer = null;
        this.view = { ...this.view, status, busy: false, step: "", stepArgs: [], lines: [...this.view.lines, line] };
        this.push();
    }
}
