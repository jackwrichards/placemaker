// s22: the map preview legend — a small window beside the bottom of the panel listing the preview layers of the plan.
// Hover an entry → the map shows only that layer; click → hide / show it; "All" → every layer back to its default.
// Blocked-for-zoning cells and decals start hidden. Runs on the fake planner (stable) and on the real Core host.
import { expect, Page, test } from "@playwright/test";
import { drawPolygon, FIELD, noTextOverflow, open, panelShot, shot, waitPlanned, pickType } from "./helpers";

async function toDesign(page: Page, recipe: string) {
    await drawPolygon(page, FIELD);
    await page.getByTestId("primary").click();
    await pickType(page, recipe);
    await waitPlanned(page);
}

const drawn = (page: Page) => page.evaluate(() => (window as any).pm.backend.drawnLayers as string[]);

async function drawnSoon(page: Page, test: (layers: string[]) => boolean) {
    await expect.poll(async () => test(await drawn(page)), { timeout: 3000 }).toBe(true);
}

test("legend: layers of the plan, hover isolates one, click hides it, All resets", async ({ page }) => {
    const errors = await open(page);
    await page.evaluate(() => localStorage.removeItem("placemaker.legendOpen.v1"));
    await toDesign(page, "park");

    const legend = page.getByTestId("legend-window");
    await expect(legend).toBeVisible();
    await expect(legend).toHaveAttribute("data-open", "true");
    // The fake park has trees and lawn at least; every row has a swatch, a name and one short number.
    const trees = legend.getByTestId("legend-row-trees");
    await expect(trees).toBeVisible();
    await expect(trees).toHaveAttribute("data-visible", "true");
    await expect(legend.getByTestId("legend-row-surfaces")).toBeVisible();
    await drawnSoon(page, (l) => l.includes("trees") && l.includes("surfaces"));
    await noTextOverflow(page, "legend");
    await panelShot(page, "s22-legend");

    // Hover: only that layer is drawn.
    await trees.hover();
    await expect.poll(() => page.evaluate(() => (window as any).pm.backend.legendHover)).toBe("trees");
    await drawnSoon(page, (l) => l.length === 1 && l[0] === "trees");
    await shot(page, "s22-legend-hover-trees");
    // Leaving the window shows everything again.
    await page.mouse.move(5, 300);
    await expect.poll(() => page.evaluate(() => (window as any).pm.backend.legendHover)).toBe("");
    await drawnSoon(page, (l) => l.includes("trees") && l.includes("surfaces"));

    // Click: hidden (row greyed, map without it), kept across a re-plan; click again shows it.
    await trees.click();
    await expect(trees).toHaveAttribute("data-visible", "false");
    await page.mouse.move(5, 300);
    await drawnSoon(page, (l) => !l.includes("trees") && l.includes("surfaces"));
    await expect(legend.getByTestId("legend-reset")).toBeVisible();
    await trees.click();
    await expect(trees).toHaveAttribute("data-visible", "true");
    await expect(legend.getByTestId("legend-reset")).toHaveCount(0);

    // All: every layer back to its default after several changes.
    await trees.click();
    await legend.getByTestId("legend-row-surfaces").click();
    await expect(legend.getByTestId("legend-row-surfaces")).toHaveAttribute("data-visible", "false");
    await legend.getByTestId("legend-reset").click();
    await expect(trees).toHaveAttribute("data-visible", "true");
    await expect(legend.getByTestId("legend-row-surfaces")).toHaveAttribute("data-visible", "true");
    await page.mouse.move(5, 300);
    await drawnSoon(page, (l) => l.includes("trees") && l.includes("surfaces"));

    // Collapsible (remembered).
    await legend.getByTestId("legend-toggle").click();
    await expect(legend).toHaveAttribute("data-open", "false");
    await expect(legend.getByTestId("legend-rows")).toHaveCount(0);
    await legend.getByTestId("legend-toggle").click();
    await expect(legend).toHaveAttribute("data-open", "true");

    // Area overhaul: in the Area stage the window lists what the area tools draw, not the plan's layers.
    await page.getByTestId("stagebar-area").click();
    await expect(legend.getByTestId("legend-row-trees")).toHaveCount(0);
    await expect(legend.getByTestId("legend-row-area:outline")).toBeVisible();
    expect(errors).toEqual([]);
});

test("legend: default-off layers start hidden and show on click (real Core host)", async ({ page }) => {
    await open(page, { real: true });
    await page.waitForFunction(() => (window as any).pm.backend.hostUp === true, null, { timeout: 8000 }).catch(() => undefined);
    const up = await page.evaluate(() => (window as any).pm.backend.hostUp === true);
    test.skip(!up, "mock host not running");
    await toDesign(page, "park");
    await page.waitForFunction(() => (window as any).pm.backend.plan?.source === "core");
    // (sketch study) the real host sends the site plan: the top view replaces this window (siteplan.spec.ts); the legend
    // window stays the Classic sketch style's, covered above on the fake planner
    await page.waitForTimeout(300);
    test.skip((await page.getByTestId("siteplan-window").count()) > 0, "site plan style: the top view replaces the legend window");
    const legend = page.getByTestId("legend-window");
    await expect(legend).toBeVisible();
    // Core sends the legend: every row's layer is one of the known ids, roads grouped, defaults applied.
    const ids: string[] = await page.evaluate(() => ((window as any).pm.backend.summary().legend ?? []).map((e: any) => e.id));
    expect(ids.length).toBeGreaterThan(0);
    const decals = legend.getByTestId("legend-row-decals");
    if (ids.includes("decals")) {
        await expect(decals).toHaveAttribute("data-visible", "false");
        await decals.click();
        await expect(decals).toHaveAttribute("data-visible", "true");
        await page.mouse.move(5, 300);
        await drawnSoon(page, (l) => l.includes("decals"));
        await legend.getByTestId("legend-reset").click();
        await expect(decals).toHaveAttribute("data-visible", "false");
    }
    // Hover isolates with the host's own layer tags too.
    const first = ids.find((i) => i !== "decals" && i !== "blocked" && i !== "terrain" && i !== "entrances" && i !== "cutouts")!;
    await legend.getByTestId(`legend-row-${first}`).hover();
    await drawnSoon(page, (l) => l.length === 1 && l[0] === first);
    await page.mouse.move(5, 300);
});
