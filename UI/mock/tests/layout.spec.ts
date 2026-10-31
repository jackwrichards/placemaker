import { expect, Page, test } from "@playwright/test";
import { execFileSync } from "child_process";
import fs from "fs";
import path from "path";
import { lintCss } from "../../tools/css-lint";
import { clickWorld, drawPolygon, FIELD, open, shot, waitPlanned, pickType } from "./helpers";

const UI = path.join(__dirname, "..", "..");

async function noHorizontalOverflow(page: Page) {
    const res = await page.evaluate(() => {
        const panel = document.querySelector('[data-testid="placemaker-panel"]') as HTMLElement;
        const r = panel.getBoundingClientRect();
        const bad: string[] = [];
        panel.querySelectorAll("*").forEach((el) => {
            const e = el as HTMLElement;
            const b = e.getBoundingClientRect();
            if (b.width === 0 || b.height === 0) return;
            if (b.right > r.right + 1 || b.left < r.left - 1) bad.push(`${e.className || e.tagName} ${Math.round(b.left)}..${Math.round(b.right)}`);
        });
        return {
            page: document.documentElement.scrollWidth <= window.innerWidth,
            inViewport: r.left >= 0 && r.right <= window.innerWidth + 0.5 && r.top >= 0 && r.bottom <= window.innerHeight + 0.5,
            panelScroll: panel.scrollWidth <= panel.clientWidth + 1,
            bad: bad.slice(0, 5),
        };
    });
    expect(res.bad).toEqual([]);
    expect(res.page).toBe(true);
    expect(res.inViewport).toBe(true);
    expect(res.panelScroll).toBe(true);
}

for (const [w, h] of [
    [1280, 720],
    [3840, 2160],
]) {
    test(`no horizontal overflow at ${w}×${h} (rem scaling) in every stage`, async ({ page }) => {
        await open(page, { width: w, height: h });
        await noHorizontalOverflow(page);
        await drawPolygon(page, FIELD);
        await noHorizontalOverflow(page);
        await page.getByTestId("primary").click();
        await pickType(page, "park");
        await page.getByTestId("feature-toggle-playground").click();
        await waitPlanned(page);
        await noHorizontalOverflow(page);
        await shot(page, `layout-${w}x${h}-design`);
        await page.getByTestId("primary").click();
        await waitPlanned(page);
        await noHorizontalOverflow(page);
        await shot(page, `layout-${w}x${h}-review`);
    });
}

test("Gameface CSS lint passes on the bundle (and catches forbidden constructs)", async () => {
    const css = fs.readFileSync(path.join(UI, "mock", "dist", "mock.css"), "utf8");
    expect(lintCss(css)).toEqual([]);
    const bad = lintCss(".a{display:grid;color:hsla(0,0%,0%,1);margin:var(--x);transition:all .2s}.b:not(.c){width:clamp(1rem,2rem,3rem);gap:4rem}");
    const msgs = bad.map((p) => p.msg).join("\n");
    for (const needle of ["grid", "hsl", "shorthand 'margin'", "'all'", ":not()", "clamp()", "gap"]) expect(msgs).toContain(needle);
});

test("English strings in Lang/en-US.ui.json are in sync with the code", async () => {
    const out = execFileSync("node", [path.join(UI, "tools", "extract-lang.js"), "--check"], { encoding: "utf8" });
    expect(out).toContain("up to date");
});

test("area modes: pick block (+shift), paint, road loop", async ({ page }) => {
    await open(page);
    await page.getByTestId("area-mode-block").click();
    await clickWorld(page, 420, 320);
    await clickWorld(page, 570, 320, { modifiers: ["Shift"] });
    expect(await page.evaluate(() => (window as any).pm.backend.state().area.valid)).toBe(true);
    await shot(page, "10-mode-block");

    await page.getByTestId("area-mode-paint").click();
    await expect(page.getByTestId("brush")).toBeVisible();
    await page.getByTestId("brush-shape-square").click();
    await expect(page.getByTestId("brush-angle")).toBeVisible();
    const [x, y] = await page.evaluate(() => (window as any).pm.toScreen([1000, 300]));
    await page.mouse.move(x, y);
    await page.mouse.down();
    for (let i = 1; i <= 10; i++) await page.mouse.move(x + i * 12, y + i * 6);
    await page.mouse.up();
    const painted = await page.evaluate(() => (window as any).pm.backend.state().area);
    expect(painted.closed).toBe(true);
    expect(painted.areaM2).toBeGreaterThan(1000);
    await shot(page, "11-mode-paint");

    await page.getByTestId("clear-area").click();
    // Area overhaul: Road nodes is part of Points (corners snap to road nodes in the game).
    await page.getByTestId("area-mode-draw").click();
    for (const [nx, nz] of [
        [490, 260],
        [790, 260],
        [790, 490],
        [490, 490],
        [490, 260],
    ])
        await clickWorld(page, nx, nz);
    expect(await page.evaluate(() => (window as any).pm.backend.state().area.valid)).toBe(true);
    await shot(page, "12-mode-points-on-nodes");
});

test("launcher toggles the tool; info-panel section: no Edit (it left an edit open), Ungroup asks in place", async ({ page }) => {
    await open(page);
    await expect(page.getByTestId("launcher")).toHaveAttribute("data-open", "true");
    await page.getByTestId("launcher").locator("button").click();
    await expect(page.getByTestId("placemaker-panel")).toHaveCount(0);
    await expect(page.getByTestId("launcher")).toHaveAttribute("data-open", "false");
    await page.getByTestId("launcher").locator("button").click();
    await expect(page.getByTestId("placemaker-panel")).toBeVisible();

    await drawPolygon(page, FIELD);
    await page.getByTestId("primary").click();
    await page.getByTestId("primary").click();
    await waitPlanned(page);
    await page.getByTestId("primary").click();
    await expect(page.getByTestId("stage-area")).toBeVisible({ timeout: 10000 });
    await page.getByTestId("close").click();
    await clickWorld(page, 650, 380);
    await expect(page.getByTestId("info-section")).toBeVisible();
    await shot(page, "13-info-section");
    // (Phase 1) Edit · Rename · Ungroup · Remove: Edit is back first, and it opens Placemaker on the build (C# EditGroup
    // activates the tool, so an edit can never linger unseen as on 22 Oct)
    await expect(page.getByTestId("info-edit")).toBeVisible();
    const order = await page.getByTestId("info-section").evaluate((el) =>
        Array.from(el.querySelectorAll("[data-testid=info-edit],[data-testid=info-rename],[data-testid=info-ungroup],[data-testid=info-remove]")).map((b) => b.getAttribute("data-testid")),
    );
    expect(order).toEqual(["info-edit", "info-rename", "info-ungroup", "info-remove"]);
    // Ungroup asks in the section itself, no dialog
    await page.getByTestId("info-ungroup").click();
    await expect(page.getByTestId("info-confirm")).toBeVisible();
    await expect(page.getByTestId("confirm-dialog")).toHaveCount(0);
    await page.getByTestId("info-confirm-cancel").click();
    await expect(page.getByTestId("info-confirm")).toHaveCount(0);
    await expect(page.getByTestId("info-remove")).toBeVisible();
    await page.getByTestId("info-edit").click();
    await expect(page.getByTestId("placemaker-panel")).toBeVisible();
    await expect(page.getByTestId("stage-design")).toBeVisible();
});

test("robustness: malformed or partial C# payloads never crash the panel", async ({ page }) => {
    const errors = await open(page);
    // publish garbage directly into the bindings, as a buggy or older C# side might
    await page.evaluate(() => (window as any).pmPublish("Placemaker", "catalog", "{not json"));
    await page.evaluate(() => (window as any).pmPublish("Placemaker", "design", JSON.stringify({ root: { layer: "nope", children: "x" } })));
    await page.evaluate(() => (window as any).pmPublish("Placemaker", "summary", JSON.stringify({ ready: true, stats: null, issues: [{ severity: "error" }] })));
    await page.evaluate(() => (window as any).pmPublish("Placemaker", "state", JSON.stringify({ open: true, stage: "review", primary: null })));
    await expect(page.getByTestId("placemaker-panel")).toBeVisible();
    await expect(page.locator("[data-pm-crash]")).toHaveCount(0);
    await page.evaluate(() => (window as any).pmPublish("Placemaker", "state", JSON.stringify({ open: true, stage: "design" })));
    await expect(page.getByTestId("placemaker-panel")).toBeVisible();
    expect(errors.filter((e) => !e.includes("bad JSON"))).toEqual([]);
});
