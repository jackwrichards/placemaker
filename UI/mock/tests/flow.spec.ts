import { expect, test } from "@playwright/test";
import { clickWorld, drawPolygon, FIELD, open, panelShot, shot, stage, waitPlanned, pickType, unfoldSections } from "./helpers";

test("full flow: draw → design → review → build, then manage the result", async ({ page }) => {
    const errors = await open(page);
    await shot(page, "01-open-empty");

    const primary = page.getByTestId("primary");
    await expect(primary).toHaveAttribute("aria-disabled", "true");

    // drawing collapses the panel to one row
    await clickWorld(page, FIELD[0][0], FIELD[0][1]);
    await clickWorld(page, FIELD[1][0], FIELD[1][1]);
    await expect(page.getByTestId("panel-compact")).toBeVisible();
    expect((await primary.boundingBox())!.width).toBeGreaterThan(100);
    await shot(page, "02-drawing-compact");
    await clickWorld(page, FIELD[2][0], FIELD[2][1]);
    await clickWorld(page, FIELD[3][0], FIELD[3][1]);
    await clickWorld(page, FIELD[0][0], FIELD[0][1]);
    await expect(page.getByTestId("panel-compact")).toBeHidden();
    await expect(page.getByTestId("area-summary")).toContainText("ha");
    await page.getByTestId("undo").hover();
    await expect(page.getByTestId("tooltip")).toContainText("Close outline");
    await shot(page, "03-area-closed");

    await expect(primary).toHaveAttribute("aria-disabled", "false");
    await primary.click();
    expect(await stage(page)).toBe("design");
    await waitPlanned(page);
    await expect(page.getByTestId("step-0")).toBeVisible();
    // single-layer recipe: no tree chrome
    await expect(page.getByTestId("layer-tree")).toHaveCount(0);
    await shot(page, "04-design-park");
    await panelShot(page, "panel-design-park");

    await page.keyboard.press("Enter");
    expect(await stage(page)).toBe("review");
    await waitPlanned(page);
    await expect(page.getByTestId("review-cost")).toContainText("¢");
    await expect(page.getByTestId("review-stats")).toBeVisible();
    await shot(page, "05-review");
    await panelShot(page, "panel-review");

    await expect(primary).toContainText("Build");
    // Keep area off: Build builds, then Placemaker closes (Keep area on, the default, has its own test)
    await page.getByTestId("build-keep").click();
    await expect(page.getByTestId("build-keep")).toHaveAttribute("aria-checked", "false");
    await primary.click();
    await expect(page.getByTestId("stage-building")).toBeVisible();
    await panelShot(page, "panel-building");
    await expect(page.getByTestId("launcher")).toHaveAttribute("data-open", "false", { timeout: 10000 });
    // built: area cleared, no type kept, group on the map
    expect(await page.evaluate(() => (window as any).pm.backend.groups.length)).toBe(1);
    expect(await page.evaluate(() => (window as any).pm.backend.ring.length)).toBe(0);
    expect(await page.evaluate(() => (window as any).pm.backend.root)).toBeNull();
    await page.getByTestId("launcher").locator("button").click();
    await expect(page.getByTestId("stage-area")).toBeVisible();
    await shot(page, "06-built");

    // manage: click the built group
    await clickWorld(page, 650, 380);
    await expect(page.getByTestId("stage-manage")).toBeVisible();
    await page.getByTestId("manage-rename").click();
    await page.getByTestId("rename-input").fill("Riverside Park");
    await page.getByTestId("rename-save").click();
    await expect(page.getByTestId("manage-name")).toHaveText("Riverside Park");
    await panelShot(page, "panel-manage");
    await page.getByTestId("manage-remove").click();
    await expect(page.getByTestId("confirm-dialog")).toBeVisible();
    await shot(page, "07-remove-confirm");
    await page.getByTestId("confirm-ok").click();
    expect(await page.evaluate(() => (window as any).pm.backend.groups.length)).toBe(0);

    expect(errors).toEqual([]);
});

test("Build & keep area: the area stays, back to Design with no type chosen", async ({ page }) => {
    const errors = await open(page);
    await drawPolygon(page, FIELD);
    await page.getByTestId("primary").click();
    expect(await stage(page)).toBe("design");
    await waitPlanned(page);
    await page.keyboard.press("Enter");
    expect(await stage(page)).toBe("review");
    await waitPlanned(page);
    await expect(page.getByTestId("primary")).toContainText("Build");
    await expect(page.getByTestId("build-keep")).toHaveAttribute("aria-checked", "true");
    await page.getByTestId("primary").click();
    await expect(page.getByTestId("stage-building")).toBeVisible();
    await expect(page.getByTestId("stage-design")).toBeVisible({ timeout: 10000 });
    expect(await page.evaluate(() => (window as any).pm.backend.groups.length)).toBe(1);
    expect(await page.evaluate(() => (window as any).pm.backend.ring.length)).toBe(4);
    expect(await page.evaluate(() => (window as any).pm.backend.root)).toBeNull();
    await expect(page.getByTestId("primary")).toHaveAttribute("aria-disabled", "true");
    await expect(page.getByTestId("launcher")).toHaveAttribute("data-open", "true");
    expect(errors).toEqual([]);
});

test("Review's One group switch; Ungroup in Manage (asks first, for good)", async ({ page }) => {
    const errors = await open(page);
    await drawPolygon(page, FIELD);
    await page.getByTestId("primary").click();
    await waitPlanned(page);
    await page.keyboard.press("Enter");
    expect(await stage(page)).toBe("review");
    await waitPlanned(page);
    // One group is on by default; off = built as separate pieces
    await expect(page.getByTestId("build-group")).toHaveAttribute("aria-checked", "true");
    await page.getByTestId("build-group").click();
    await expect(page.getByTestId("build-group")).toHaveAttribute("aria-checked", "false");
    expect(await page.evaluate(() => (window as any).pm.backend.buildSeparate)).toBe(true);
    await panelShot(page, "panel-review-separate");
    await page.getByTestId("build-group").click();
    await page.getByTestId("build-keep").click();
    await page.getByTestId("primary").click();
    await expect(page.getByTestId("launcher")).toHaveAttribute("data-open", "false", { timeout: 10000 });
    await page.getByTestId("launcher").locator("button").click();
    await expect(page.getByTestId("stage-area")).toBeVisible();
    await clickWorld(page, 650, 380);
    await expect(page.getByTestId("stage-manage")).toBeVisible();
    // Ungroup asks first and says it cannot be undone; then the build is gone from Manage (dissolved into plain pieces)
    const groups = await page.evaluate(() => (window as any).pm.backend.groups.length);
    await page.getByTestId("manage-ungroup").click();
    await expect(page.getByTestId("confirm-dialog")).toBeVisible();
    await expect(page.getByTestId("confirm-dialog")).toContainText("can't be undone");
    await panelShot(page, "panel-ungroup-confirm");
    await page.getByTestId("confirm-ok").click();
    await expect(page.getByTestId("stage-manage")).toHaveCount(0);
    expect(await page.evaluate(() => (window as any).pm.backend.groups.length)).toBe(groups - 1);
    expect(errors).toEqual([]);
});

test("going back to Area keeps the design; switching recipe keeps the area", async ({ page }) => {
    await open(page);
    await drawPolygon(page, FIELD);
    await page.getByTestId("primary").click();
    await pickType(page, "plaza");
    await waitPlanned(page);
    await unfoldSections(page);
    await page.getByTestId("choice-pattern-grid").click();
    await expect(page.getByTestId("choice-pattern-grid")).toHaveAttribute("aria-pressed", "true");

    // back to Area via the stage bar
    await page.getByTestId("stagebar-area").click();
    expect(await stage(page)).toBe("area");
    // adjust the shape
    const before = await page.evaluate(() => (window as any).pm.backend.ring.length);
    expect(before).toBe(4);
    // forward again: the design is intact
    await page.getByTestId("stagebar-design").click();
    expect(await stage(page)).toBe("design");
    await expect(page.getByTestId("recipe-plaza")).toHaveAttribute("aria-pressed", "true");
    await expect(page.getByTestId("choice-pattern-grid")).toHaveAttribute("aria-pressed", "true");

    // switching recipe keeps the area
    const area = await page.evaluate(() => (window as any).pm.backend.state().area.areaM2);
    await pickType(page, "park");
    await waitPlanned(page);
    expect(await page.evaluate(() => (window as any).pm.backend.state().area.areaM2)).toBe(area);
    // and undo brings the plaza back
    await page.getByTestId("undo").click();
    await expect(page.getByTestId("recipe-plaza")).toHaveAttribute("aria-pressed", "true");
});

test("stage bar: unreachable stages are locked, reachable ones roll back", async ({ page }) => {
    await open(page);
    await expect(page.getByTestId("stagebar-design")).toHaveAttribute("data-state", "locked");
    await expect(page.getByTestId("stagebar-review")).toHaveAttribute("data-state", "locked");
    await drawPolygon(page, FIELD);
    await expect(page.getByTestId("stagebar-design")).toHaveAttribute("data-state", "reachable");
    await page.getByTestId("stagebar-review").click();
    expect(await stage(page)).toBe("review");
    await expect(page.getByTestId("stagebar-area")).toHaveAttribute("data-state", "reachable");
    await expect(page.getByTestId("stagebar-review")).toHaveAttribute("data-state", "current");
});
