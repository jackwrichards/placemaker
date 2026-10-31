// Shared helpers for the mock UI tests.
import { expect, Page } from "@playwright/test";
import fs from "fs";
import path from "path";

export const SHOTS = path.join(__dirname, "..", "screenshots");

/** Opens the mock. Default: fake catalog (stable for UX tests); { real: true } uses the Core host. */
export async function open(page: Page, opts: { width?: number; height?: number; real?: boolean; lang?: string } = {}) {
    if (opts.width && opts.height) await page.setViewportSize({ width: opts.width, height: opts.height });
    const errors: string[] = [];
    page.on("pageerror", (e) => errors.push(String(e)));
    page.on("console", (m) => {
        // Vanilla game icons ("Media/Game/Icons/X.svg", relative like vanilla) don't exist in the browser:
        // the Icon component falls back to our glyph, so their 404s are expected here.
        if (m.type() === "error" && !(m.location()?.url ?? "").includes("/Media/")) errors.push(m.text());
    });
    const lang = opts.lang && opts.lang !== "en-US" ? "lang=" + opts.lang : "";
    await page.goto(opts.real ? "/" + (lang ? "?" + lang : "") : "/?fake=1" + (lang ? "&" + lang : ""));
    await page.evaluate(() => {
        try {
            localStorage.removeItem("placemaker.panelPos.v2");
            localStorage.removeItem("placemaker.panelWidth.v1");
        } catch {
            /* ignore */
        }
    });
    await expect(page.getByTestId("placemaker-panel")).toBeVisible();
    // make builds quick and deterministic for tests
    await page.evaluate(() => ((window as any).pm.backend.buildStepMs = 40));
    return errors;
}

/** World metres → page pixels (via the map's own transform). */
export async function screen(page: Page, x: number, z: number): Promise<[number, number]> {
    return page.evaluate(([wx, wz]) => (window as any).pm.toScreen([wx, wz]), [x, z]);
}

export async function clickWorld(page: Page, x: number, z: number, opts: { button?: "left" | "right"; modifiers?: ("Shift" | "Control")[] } = {}) {
    const [px, py] = await screen(page, x, z);
    for (const m of opts.modifiers ?? []) await page.keyboard.down(m);
    await page.mouse.click(px, py, { button: opts.button ?? "left" });
    for (const m of opts.modifiers ?? []) await page.keyboard.up(m);
}

/** Draws a closed polygon by clicking its corners and then the first corner again. */
export async function drawPolygon(page: Page, pts: [number, number][]) {
    for (const [x, z] of pts) await clickWorld(page, x, z);
    await clickWorld(page, pts[0][0], pts[0][1]);
}

/** An empty field in the fake city (blocks 3..5 × 2..4 are empty). */
export const FIELD: [number, number][] = [
    [520, 290],
    [770, 270],
    [800, 470],
    [540, 500],
];

/**
 * Waits for the plan. (Phase 1) A type that needs an entrance plans nothing until it has one: by default an entrance is placed on
 * the outline first, as a player does; <code>entrances: false</code> leaves it missing (the wait then ends at once).
 */
export async function waitPlanned(page: Page, entrances = true) {
    await page.waitForTimeout(50);
    const needs = await page.evaluate(() => (window as any).pm.backend.needsEntrances());
    if (needs && !entrances) return;
    if (needs) await page.evaluate(() => (window as any).pm.backend.autoEntrances());
    await page.waitForFunction(() => {
        const b = (window as any).pm.backend;
        return !!b.plan && !b.planning && !b.stale;
    });
}

export async function shot(page: Page, name: string) {
    fs.mkdirSync(SHOTS, { recursive: true });
    await page.mouse.move(5, 300);
    await page.waitForTimeout(250);
    await page.screenshot({ path: path.join(SHOTS, name + ".png") });
}

/** The panel (and a picker flyout beside it, if open) with a little map around it. */
export async function panelShot(page: Page, name: string) {
    fs.mkdirSync(SHOTS, { recursive: true });
    await page.mouse.move(5, 300);
    await page.waitForTimeout(250);
    const box = await page.getByTestId("placemaker-panel").boundingBox();
    if (!box) throw new Error("panel not visible");
    const flyout = page.getByTestId("picker-flyout");
    const fly = (await flyout.count()) ? await flyout.boundingBox() : null;
    const vp = page.viewportSize()!;
    const pad = 20;
    const x0 = Math.max(0, Math.min(box.x, fly ? fly.x : box.x) - pad);
    const x1 = Math.min(vp.width, Math.max(box.x + box.width, fly ? fly.x + fly.width : 0) + pad);
    const y0 = Math.max(0, box.y - pad);
    const y1 = Math.min(vp.height, box.y + box.height + pad);
    await page.screenshot({ path: path.join(SHOTS, name + ".png"), clip: { x: x0, y: y0, width: x1 - x0, height: y1 - y0 } });
}

export function stage(page: Page) {
    return page.getByTestId("placemaker-panel").getAttribute("data-stage");
}

/**
 * Every text in the panel fits its box (scrollWidth ≤ clientWidth: nothing cut or ellipsised) and
 * stays inside each of its ancestors horizontally (nothing spills onto a neighbour, as the card
 * descriptions did in Gameface).
 */
export async function noTextOverflow(page: Page, where: string) {
    const bad = await page.evaluate(() => {
        const root = document.querySelector('[data-testid="placemaker-wrap"]') as HTMLElement;
        const out: string[] = [];
        const seen = new Set<Element>();
        const walker = document.createTreeWalker(root, NodeFilter.SHOW_TEXT);
        while (walker.nextNode()) {
            const node = walker.currentNode;
            const text = (node.textContent ?? "").trim();
            const el = node.parentElement;
            if (!text || !el || seen.has(el)) continue;
            seen.add(el);
            const r = el.getBoundingClientRect();
            if (r.width === 0 || r.height === 0) continue;
            const inline = getComputedStyle(el).display === "inline";
            // data-ellipsis: a deliberate one-line cut whose full text is in the tooltip (Review issues).
            if (!inline && !el.closest("[data-ellipsis]") && el.scrollWidth > el.clientWidth + 1) out.push(`cut: "${text}" (${el.scrollWidth} > ${el.clientWidth})`);
            for (let a = el.parentElement; a && a !== root.parentElement; a = a.parentElement) {
                const ar = a.getBoundingClientRect();
                if (ar.width === 0) continue;
                if (r.left < ar.left - 1 || r.right > ar.right + 1) {
                    out.push(`spills: "${text}" out of ${String(a.className).split(" ")[0] || a.tagName}`);
                    break;
                }
                // the panel and the windows beside it (picker, legend, top view) are the outermost boxes
                const id = a.getAttribute("data-testid");
                if (id === "placemaker-panel" || id === "picker-flyout" || id === "legend-window" || id === "siteplan-window" || id === "siteplan-flyout") break;
            }
        }
        return out;
    });
    expect(bad, where).toEqual([]);
}


/**
 * (s42) Selects a type in the picker: its category chip first (opening "N more" when the category is hidden), then
 * the type chip. Picking an already selected type does nothing.
 */
export async function pickType(page: Page, id: string) {
    const chip = page.getByTestId(`recipe-${id}`);
    if (!(await chip.count())) {
        const group: string = await page.evaluate((r) => {
            const b = (window as any).pm.backend;
            const rec = b.recipes.find((x: any) => x.info.id === r);
            return rec?.info.group ?? "";
        }, id);
        const cat = page.getByTestId(`make-group-${group}`);
        if (!(await cat.count()) && (await page.getByTestId("make-groups-more").count())) await page.getByTestId("make-groups-more").click();
        if (await cat.count()) await cat.click();
        else {
            // no declared group: walk the categories until the type shows up
            const all = await page.locator('[data-testid^="make-group-"][data-nav]').evaluateAll((els) => els.map((e) => e.getAttribute("data-testid")!));
            for (const c of all) {
                await page.getByTestId(c).click();
                if (await chip.count()) break;
            }
        }
    }
    await chip.click();
}

/** (s42) Shape sections start folded: unfold every section head of the type (not its extras' options). */
export async function unfoldSections(page: Page) {
    for (let i = 0; i < 20; i++) {
        const closed = page.locator('[data-testid="shape"] [data-testid$="-toggle"][aria-expanded="false"]');
        if (!(await closed.count())) return;
        await closed.first().click();
    }
}
