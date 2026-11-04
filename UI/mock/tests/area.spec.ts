// Area overhaul (PROTOCOL 24): three tools (Road nodes folded into Points), ONE "Stops at" for every tool with zoning as
// one choice, the bucket's Gaps and Reach, Paint area's One block / Gaps, and the legend window while choosing the area.
// Screenshots: mock/screenshots/area-*.png.
import { expect, test } from "@playwright/test";
import { noTextOverflow, open, panelShot } from "./helpers";

const backend = (page: any) => page.evaluate(() => (window as any).pm.backend.brushRules);

test("area: three tools, one set of stops shared by every tool", async ({ page }) => {
    const errors = await open(page);
    await expect(page.getByTestId("area-mode-draw")).toBeVisible();
    await expect(page.getByTestId("area-mode-block")).toBeVisible();
    await expect(page.getByTestId("area-mode-paint")).toBeVisible();
    await expect(page.getByTestId("area-mode-perimeter")).toHaveCount(0);

    // Points: the stops and the road-node note.
    await expect(page.getByTestId("points-settings")).toBeVisible();
    for (const k of ["roads", "paths", "bikes", "tracks", "water", "buildings", "lots", "builds"]) await expect(page.getByTestId(`stop-${k}`)).toHaveAttribute("aria-pressed", "true");
    await expect(page.getByTestId("stop-zoning-2")).toHaveAttribute("aria-pressed", "true");
    await expect(page.getByTestId("points-note")).toBeVisible();
    await noTextOverflow(page, "points");
    await panelShot(page, "area-points");

    // A stop toggled in Points is the bucket's too.
    await page.getByTestId("stop-paths").click();
    expect((await backend(page)).paths).toBe(false);
    await page.getByTestId("area-mode-block").click();
    await expect(page.getByTestId("bucket-settings")).toBeVisible();
    await expect(page.getByTestId("stop-paths")).toHaveAttribute("aria-pressed", "false");
    await expect(page.getByTestId("area-gap")).toBeVisible();
    await expect(page.getByTestId("area-reach")).toBeVisible();
    await noTextOverflow(page, "bucket");
    await panelShot(page, "area-bucket");

    // Zoning as one choice sets the three switches.
    await page.getByTestId("stop-zoning-1").click();
    let r = await backend(page);
    expect([r.zoned, r.allZoned, r.emptyZones, r.zoning]).toEqual([true, false, false, 1]);
    await page.getByTestId("stop-zoning-3").click();
    r = await backend(page);
    expect([r.zoned, r.allZoned, r.emptyZones]).toEqual([true, true, true]);
    await page.getByTestId("stop-zoning-0").click();
    r = await backend(page);
    expect([r.zoned, r.allZoned, r.emptyZones]).toEqual([false, false, false]);

    // Paint area: the same stops, greyed with Smart edges off; One block off greys the gap.
    await page.getByTestId("area-mode-paint").click();
    await expect(page.getByTestId("smart-edges")).toHaveAttribute("aria-checked", "true");
    await expect(page.getByTestId("stop-paths")).toHaveAttribute("aria-pressed", "false");
    await expect(page.getByTestId("area-gap")).not.toHaveAttribute("data-disabled", "true");
    await page.getByTestId("brush-rule-stayInBlock").click();
    await expect(page.getByTestId("area-gap")).toHaveAttribute("data-disabled", "true");
    await page.getByTestId("brush-rule-stayInBlock").click();
    await noTextOverflow(page, "paint area");
    await panelShot(page, "area-paint");
    await page.getByTestId("smart-edges").click();
    await expect(page.getByTestId("stop-roads")).toHaveAttribute("aria-disabled", "true");
    await page.getByTestId("smart-edges").click();

    // Reset brings every stop back.
    await page.getByTestId("brush-rules-reset").click();
    r = await backend(page);
    expect([r.paths, r.zoning, r.gap, r.reach]).toEqual([true, 2, 10, 500]);
    expect(errors).toEqual([]);
});

test("area: an old saved Road nodes mode opens Points", async ({ page }) => {
    const errors = await open(page);
    await page.evaluate(() => (window as any).pm.backend.onTrigger("setAreaMode", ["perimeter"]));
    await expect(page.getByTestId("area-mode-draw")).toHaveAttribute("aria-pressed", "true");
    expect(errors).toEqual([]);
});

test("area: the legend window lists what the tool draws", async ({ page }) => {
    const errors = await open(page);
    await page.getByTestId("area-mode-block").click();
    await expect(page.getByTestId("legend-window")).toBeVisible();
    await expect(page.getByTestId("legend-row-area:doors")).toBeVisible();
    await expect(page.getByTestId("legend-row-area:stops")).toBeVisible();
    await page.getByTestId("legend-row-area:stops").click();
    expect(await page.evaluate(() => (window as any).pm.backend.legendVisibility["area:stops"])).toBe(false);
    await panelShot(page, "area-legend");
    expect(errors).toEqual([]);
});
