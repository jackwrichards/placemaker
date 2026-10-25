// s42 — the Design stage as picker + editing bar + Shape / Extras grids, the compact Review and Area stages.
// Screenshots of real Core types at the default panel width land in mock/screenshots/s42-*.png.
import { expect, Page, test } from "@playwright/test";
import { drawPolygon, FIELD, noTextOverflow, open, panelShot, waitPlanned, pickType } from "./helpers";

async function realDesign(page: Page) {
    const errors = await open(page, { real: true });
    await page.waitForFunction(() => (window as any).pm.backend.hostUp !== undefined);
    await page.waitForTimeout(300);
    await page.evaluate(() => {
        localStorage.removeItem("placemaker.sections.v1");
        localStorage.removeItem("placemaker.browserCategory.v2");
        localStorage.removeItem("placemaker.categoryUse.v1");
    });
    await drawPolygon(page, FIELD);
    await page.getByTestId("primary").click();
    return errors;
}

async function pick(page: Page, group: string, recipe: string) {
    const chip = page.getByTestId(`make-group-${group}`);
    if (!(await chip.count())) await page.getByTestId("make-groups-more").click();
    await page.getByTestId(`make-group-${group}`).click();
    await pickType(page, recipe);
    await waitPlanned(page);
    await page.waitForTimeout(150);
}

test("s42 design: library rows, save strip, two-column Parameters with Add-ons for real types", async ({ page }) => {
    const errors = await realDesign(page);
    test.skip(!(await page.evaluate(() => (window as any).pm.backend.hostLayers.has("waterfront"))), "no real Core layers");

    for (const [group, recipe] of [
        ["parks", "park"],
        ["beach", "waterfront"],
        ["roads", "roads"],
        ["shops", "stripMall"],
        ["shops", "plaza"],
    ]) {
        await pick(page, group, recipe);
        await expect(page.getByTestId(`recipe-${recipe}`)).toHaveAttribute("aria-pressed", "true");
        await expect(page.getByTestId("preset-bar")).toBeVisible();
        await expect(page.getByTestId("preset-standard")).toBeVisible();
        await noTextOverflow(page, "design " + recipe);
        await panelShot(page, `s42-design-${recipe}`);
    }

    // categories: most used first, the rest behind "N more"
    const more = page.getByTestId("make-groups-more");
    if (await more.count()) {
        await more.click();
        await panelShot(page, "s42-design-categories-all");
        await more.click();
    }

    // search filters the three rows
    await page.getByTestId("browser-search-toggle").click();
    await page.getByTestId("browser-search").fill("garden");
    await expect(page.getByTestId("preset-hits")).toBeVisible();
    await noTextOverflow(page, "search");
    await panelShot(page, "s42-design-search");
    await page.getByTestId("browser-clear").click();

    // an open Shape section and an open extra with options
    await pick(page, "parks", "park");
    const first = page.locator('[data-testid="step-0"] [data-testid$="-toggle"][aria-expanded="false"]').first();
    await first.click();
    if (await page.getByTestId("feature-toggle-plaza").count()) {
        if ((await page.getByTestId("feature-plaza").getAttribute("data-enabled")) !== "true") await page.getByTestId("feature-toggle-plaza").click();
        await waitPlanned(page);
        await page.getByTestId("feature-fold-plaza").click();
        await expect(page.getByTestId("feature-options-plaza")).toBeVisible();
    }
    await noTextOverflow(page, "open sections");
    await panelShot(page, "s42-design-park-open");
    expect(errors).toEqual([]);
});

test("s42 editing bar: unsaved changes, undo changes, save as new from the strip", async ({ page }) => {
    const errors = await open(page);
    await drawPolygon(page, FIELD);
    await page.getByTestId("primary").click();
    await pickType(page, "park");
    await waitPlanned(page);
    await expect(page.getByTestId("preset-revert")).toHaveCount(0);
    await page.getByTestId("randomize").click();
    await waitPlanned(page);
    await expect(page.getByTestId("preset-modified")).toContainText("Unsaved changes");
    await panelShot(page, "s42-editing-modified");
    await page.getByTestId("preset-revert").click();
    await expect(page.getByTestId("preset-modified")).toHaveCount(0);
    await page.getByTestId("preset-save-as").click();
    await page.getByTestId("preset-name-input").fill("My park");
    await page.getByTestId("preset-name-save").click();
    await expect(page.getByTestId("preset-bar-name")).toHaveText("My park");
    expect(errors).toEqual([]);
});

async function toReviewReal(page: Page, group: string, recipe: string) {
    await pick(page, group, recipe);
    await page.evaluate(() => (window as any).pm.backend.onTrigger("goToStage", ["review"]));
    await waitPlanned(page);
    await page.waitForFunction(() => (window as any).pm.backend.plan?.source === "core");
    await expect(page.getByTestId("stage-review")).toBeVisible();
    await page.waitForTimeout(200);
}

test("s42 review: money card with one cost bar, stat chips, alert chips, park stat tiles, OK beside Build", async ({ page }) => {
    const errors = await realDesign(page);
    test.skip(!(await page.evaluate(() => (window as any).pm.backend.hostLayers.has("waterfront"))), "no real Core layers");
    for (const [group, recipe] of [
        ["parks", "park"],
        ["beach", "waterfront"],
        ["shops", "stripMall"],
        ["shops", "outdoorMall"],
    ]) {
        await toReviewReal(page, group, recipe);
        await expect(page.getByTestId("review-cost")).toContainText("¢");
        // s45: the cost bar splits by friendly category (never one "<type> 100%" bucket); one chip per stat label and alert
        const buckets = await page.locator('[data-testid^="bucket-"]').evaluateAll((els) => els.map((e) => e.getAttribute("data-testid")!));
        expect(buckets.some((b) => b.startsWith("bucket-type:"))).toBe(false);
        expect(buckets.length).toBeGreaterThan(1);
        const statLabels = await page.locator('[data-testid="review-stats"] [data-testid^="stat-"]').evaluateAll((els) => els.map((e) => (e.lastElementChild?.textContent ?? "").trim()));
        expect(new Set(statLabels).size).toBe(statLabels.length);
        const alertTexts = await page.locator('[data-testid="issues"] button').evaluateAll((els) => els.map((e) => (e.textContent ?? "").trim()));
        expect(new Set(alertTexts).size).toBe(alertTexts.length);
        // alerts are short chips: never cut, full sentence in the tooltip
        await noTextOverflow(page, "review " + recipe);
        await panelShot(page, `s42-review-${recipe}`);
        await page.getByTestId("stagebar-design").click();
    }
    // park stats: tiles folded, Edit unfolds the sliders
    await toReviewReal(page, "parks", "park");
    await expect(page.getByTestId("park-tile-attractiveness")).toBeVisible();
    await expect(page.getByTestId("park-stat-attractiveness")).toHaveCount(0);
    await page.getByTestId("park-stats-toggle").click();
    await expect(page.getByTestId("park-stat-attractiveness")).toBeVisible();
    await panelShot(page, "s42-review-park-stats-edit");
    await page.getByTestId("park-stats-toggle").click();
    // the cost bar unfolds the per-step list
    await page.getByTestId("breakdown").click();
    await expect(page.getByTestId("breakdown-list")).toBeVisible();
    await page.getByTestId("breakdown").click();
    expect(errors).toEqual([]);
});

test("s42 review: permanent changes need the OK chip beside Build", async ({ page }) => {
    const errors = await open(page);
    await drawPolygon(page, FIELD);
    await page.getByTestId("primary").click();
    await pickType(page, "park");
    await page.getByTestId("preset-pond").click();
    await page.getByTestId("primary").click();
    await waitPlanned(page);
    const primary = page.getByTestId("primary");
    await expect(page.getByTestId("permanent").first()).toBeVisible();
    await expect(primary).toHaveAttribute("aria-disabled", "true");
    await expect(page.getByTestId("primary-reason")).toContainText("Click OK to build");
    await noTextOverflow(page, "review permanent");
    await panelShot(page, "s42-review-permanent");
    await page.getByTestId("ack-permanent").click();
    await expect(primary).toHaveAttribute("aria-disabled", "false");
    expect(errors).toEqual([]);
});

test("s42 area: three tool cards, Paint area with Smart edges, the stops, one summary line", async ({ page }) => {
    const errors = await open(page);
    await expect(page.getByTestId("area-mode-draw")).toHaveAttribute("aria-pressed", "true");
    await expect(page.getByTestId("stop-builds")).toBeVisible();
    await noTextOverflow(page, "area empty");
    await panelShot(page, "s42-area-points");

    await page.getByTestId("area-mode-paint").click();
    await expect(page.getByTestId("smart-edges")).toHaveAttribute("aria-checked", "true");
    expect(await page.evaluate(() => (window as any).pm.backend.mode)).toBe("blockPaint");
    await expect(page.getByTestId("stop-roads")).toHaveAttribute("aria-pressed", "true");
    await expect(page.getByTestId("brush-angle-row")).toHaveCount(0);
    await noTextOverflow(page, "paint area");
    await panelShot(page, "s42-area-paint");
    // Smart edges off = plain paint over anything: the stops grey out
    await page.getByTestId("smart-edges").click();
    expect(await page.evaluate(() => (window as any).pm.backend.mode)).toBe("paint");
    await expect(page.getByTestId("stop-roads")).toHaveAttribute("aria-disabled", "true");
    await page.getByTestId("brush-shape-square").click();
    await expect(page.getByTestId("brush-angle-row")).toBeVisible();
    await panelShot(page, "s42-area-paint-free");
    await page.getByTestId("brush-shape-round").click();
    await page.getByTestId("smart-edges").click();
    // the outline tools follow the stops whatever the brush does
    await page.getByTestId("area-mode-block").click();
    await expect(page.getByTestId("stop-builds")).not.toHaveAttribute("aria-disabled", "true");
    await panelShot(page, "s42-area-bucket");

    await page.getByTestId("area-mode-draw").click();
    await drawPolygon(page, FIELD);
    await expect(page.getByTestId("area-summary")).toContainText("ha");
    await expect(page.getByTestId("area-summary")).toContainText("Corners");
    await noTextOverflow(page, "area closed");
    await panelShot(page, "s42-area-closed");
    expect(errors).toEqual([]);
});
