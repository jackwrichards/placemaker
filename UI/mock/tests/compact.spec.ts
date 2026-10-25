// Round 3 (playtest 2): compact "engineering" density, foldable groups collapsed by default and
// remembered, single types only in the Make row, features as switches with indented options, and no
// text that overflows or is cut anywhere in the panel.
import { expect, Page, test } from "@playwright/test";
import { clickWorld, drawPolygon, FIELD, noTextOverflow, open, panelShot, shot, waitPlanned, pickType } from "./helpers";

async function toDesign(page: Page, recipe: string) {
    await drawPolygon(page, FIELD);
    await page.getByTestId("primary").click();
    await pickType(page, recipe);
    await waitPlanned(page);
}

async function expandAll(page: Page) {
    for (let i = 0; i < 20; i++) {
        const closed = page.locator('[data-testid="placemaker-panel"] button[aria-expanded="false"]');
        if (!(await closed.count())) return;
        await closed.first().click();
    }
}

test("Make: one browser of single types (category ▸ type rows), no mixes", async ({ page }) => {
    const errors = await open(page);
    await toDesign(page, "park");
    // every category's types: single types only
    const ids: string[] = [];
    for (const c of await page.locator('[data-testid="make-groups"] [data-testid^="make-group-"]').evaluateAll((els) => els.map((e) => e.getAttribute("data-testid")!))) {
        await page.getByTestId(c).click();
        ids.push(...(await page.locator('[data-testid="recipes"] [data-testid^="recipe-"]').evaluateAll((els) => els.map((e) => e.getAttribute("data-testid")!))));
    }
    expect(ids.sort()).toEqual(["recipe-campground", "recipe-park", "recipe-parking", "recipe-plaza", "recipe-roads"]);
    await page.getByTestId("make-group-parks").click();
    await expect(page.getByTestId("mixes")).toHaveCount(0);
    // hidden layers (split, pond) never get a row
    await expect(page.getByTestId("placemaker-panel")).not.toContainText("Mixes");
    // compact chips: icon tile + name on one 24rem line
    expect((await page.getByTestId("recipe-park").boundingBox())!.height).toBeLessThanOrEqual(24);
    await expect(page.getByTestId("recipe-park").locator("img")).toHaveCount(1);
    expect(errors).toEqual([]);
});

test("groups are foldable: all folded at first, summaries when folded, state remembered", async ({ page }) => {
    await open(page);
    await page.evaluate(() => localStorage.removeItem("placemaker.sections.v1"));
    await toDesign(page, "parking");
    const step = page.getByTestId("step-0");
    for (const g of ["basics", "layout", "zoning", "extras"]) await expect(step.getByTestId(`group-${g}`)).toHaveAttribute("data-open", "false");
    // folded groups show their values instead of rows
    await expect(page.getByTestId("control-aisle")).toHaveCount(0);
    await expect(step.getByTestId("group-layout-summary")).toHaveText("6.0 m");
    await expect(step.getByTestId("group-zoning-summary")).toHaveText("Off");
    await expect(step.getByTestId("group-extras-summary")).toContainText("Lanterns");
    await expect(step.getByTestId("group-extras-summary")).toContainText("¢5");

    // open Layout, close Basics → remembered across reloads and recipe switches
    await step.getByTestId("group-layout-toggle").click();
    await expect(page.getByTestId("slider-aisle-value")).toHaveText("6.0 m");
    await expect(page.getByTestId("control-angle")).toHaveCount(0);
    await expect(step.getByTestId("group-basics-summary")).toContainText("90°");
    await pickType(page, "plaza");
    await pickType(page, "parking");
    await expect(page.getByTestId("step-0").getByTestId("group-layout")).toHaveAttribute("data-open", "true");
    await expect(page.getByTestId("step-0").getByTestId("group-basics")).toHaveAttribute("data-open", "false");
    await page.reload();
    await expect(page.getByTestId("placemaker-panel")).toBeVisible();
    await toDesign(page, "parking");
    await expect(page.getByTestId("step-0").getByTestId("group-layout")).toHaveAttribute("data-open", "true");
    await expect(page.getByTestId("step-0").getByTestId("group-basics")).toHaveAttribute("data-open", "false");
    await page.evaluate(() => localStorage.removeItem("placemaker.sections.v1"));
});

test("features: switch adds the feature (row folded, one-line summary), its options fold out indented, switch off removes it from the plan", async ({ page }) => {
    const errors = await open(page);
    await page.evaluate(() => localStorage.removeItem("placemaker.sections.v1"));
    await toDesign(page, "park");
    const features = page.getByTestId("features");
    await expect(features).toBeVisible();
    for (const id of ["plaza", "playground", "pond", "picnic", "restrooms", "fountain", "bigTrees"]) await expect(page.getByTestId(`feature-${id}`)).toBeVisible();
    await expect(page.getByTestId("feature-plaza")).toContainText("Central plaza");
    await expect(page.getByTestId("feature-playground")).toHaveAttribute("data-enabled", "false");
    await expect(page.getByTestId("feature-options-playground")).toHaveCount(0);

    await page.getByTestId("feature-toggle-playground").click();
    await expect(page.getByTestId("feature-playground")).toHaveAttribute("data-enabled", "true");
    // FEEDBACK-4b #7: an enabled feature starts folded, with a one-line summary of its options
    await expect(page.getByTestId("feature-playground")).toHaveAttribute("data-open", "false");
    await expect(page.getByTestId("feature-options-playground")).toHaveCount(0);
    await expect(page.getByTestId("feature-summary-playground")).toContainText("Medium");
    await page.getByTestId("feature-fold-playground").click();
    const opts = page.getByTestId("feature-options-playground");
    await expect(opts).toBeVisible();
    await expect(page.getByTestId("feature-summary-playground")).toHaveCount(0);
    // unfolded under the row (full width), its sections collapsed by default with a summary
    const rowBox = (await page.getByTestId("feature-playground").boundingBox())!;
    const optBox = (await opts.boundingBox())!;
    expect(optBox.y).toBeGreaterThan(rowBox.y + rowBox.height - 2);
    await expect(opts.getByTestId("group-basics")).toHaveAttribute("data-open", "false");
    await expect(opts.getByTestId("group-basics-summary")).toContainText("Medium");
    await waitPlanned(page);
    let layers = await page.evaluate(() => (window as any).pm.backend.plan.layers.map((l: any) => l.layer));
    expect(layers).toContain("playground");
    // its options edit the feature step
    await opts.getByTestId("group-basics-toggle").click();
    await opts.getByTestId("choice-size-l").click();
    await expect(opts.getByTestId("choice-size-l")).toHaveAttribute("aria-pressed", "true");

    // the plaza feature's own options live on the park step: hidden from the park's groups, shown under the toggle when on
    await expect(page.getByTestId("step-0").getByTestId("group-plaza")).toHaveCount(0);
    await expect(page.getByTestId("control-plazaShape")).toHaveCount(0);
    await page.getByTestId("feature-toggle-plaza").click();
    await page.getByTestId("feature-fold-plaza").click();
    const plazaOpts = page.getByTestId("feature-options-plaza");
    for (const id of ["plazaShape", "plazaAt", "plazaSize"]) await expect(plazaOpts.getByTestId("control-" + id)).toBeVisible();
    await plazaOpts.getByTestId("choice-plazaAt-corner").click();
    await expect.poll(() => page.evaluate(() => (window as any).pm.backend.root.values.plazaAt)).toBe("corner");
    await expect(page.getByTestId("step-0").getByTestId("group-plaza")).toHaveCount(0);
    await page.getByTestId("feature-toggle-pond").click();
    await waitPlanned(page);
    layers = await page.evaluate(() => (window as any).pm.backend.plan.layers.map((l: any) => l.layer));
    expect(layers).toEqual(expect.arrayContaining(["plaza", "pond"]));
    await expect(page.getByTestId("features")).toContainText("3");
    await panelShot(page, "panel-features-park");

    // off: kept settings, gone from the plan, options hidden; undo brings it back
    await page.getByTestId("feature-toggle-playground").click();
    await expect(page.getByTestId("feature-options-playground")).toHaveCount(0);
    await waitPlanned(page);
    layers = await page.evaluate(() => (window as any).pm.backend.plan.layers.map((l: any) => l.layer));
    expect(layers).not.toContain("playground");
    await page.getByTestId("undo").click();
    await expect(page.getByTestId("feature-playground")).toHaveAttribute("data-enabled", "true");
    expect(await page.evaluate(() => (window as any).pm.backend.root.children.find((c: any) => c.feature === "playground").values.size)).toBe("l");

    // plaza: trees on by default; roads: zoning along streets
    await pickType(page, "plaza");
    await expect(page.getByTestId("feature-bigTrees")).toHaveAttribute("data-enabled", "true");
    await expect(page.getByTestId("feature-bigTrees")).toContainText("Trees");
    await expect(page.getByTestId("feature-kiosk")).toBeVisible();
    await pickType(page, "roads");
    await expect(page.getByTestId("feature-edgeZoning")).toContainText("Zoning along streets");
    await pickType(page, "parking");
    await expect(page.getByTestId("features")).toHaveCount(0);
    expect(errors).toEqual([]);
});

test("no text overflows or is cut anywhere in the panel (every stage, every type, all groups open)", async ({ page }) => {
    await open(page, { width: 1920, height: 1080 });
    await noTextOverflow(page, "area empty");
    await page.getByTestId("area-mode-paint").click();
    await noTextOverflow(page, "area paint");
    await page.getByTestId("area-mode-draw").click();
    await drawPolygon(page, FIELD);
    await noTextOverflow(page, "area closed");
    await page.getByTestId("primary").click();
    for (const id of ["parking", "plaza", "park", "roads", "campground"]) {
        await pickType(page, id);
        await waitPlanned(page);
        await noTextOverflow(page, id + " folded");
        for (const f of await page.locator('[data-testid^="feature-toggle-"]').all()) if ((await f.getAttribute("aria-checked")) === "false") await f.click();
        await expandAll(page);
        await noTextOverflow(page, id + " expanded");
    }
    await pickType(page, "parking");
    await page.getByTestId("asset-button-surface").click();
    await expect(page.getByTestId("asset-picker")).toBeVisible();
    await noTextOverflow(page, "asset picker");
    await page.keyboard.press("Escape");
    // no automatic entrances: the parking lot starts without one
    await expect(page.getByTestId("entrance-required")).toBeVisible();
    await noTextOverflow(page, "entrance required");
    await page.getByTestId("marker-kind-exit").click();
    await noTextOverflow(page, "placing markers");
    await clickWorld(page, (FIELD[1][0] + FIELD[2][0]) / 2, (FIELD[1][1] + FIELD[2][1]) / 2);
    await page.getByTestId("marker-done").click();
    await page.getByTestId("primary").click();
    await waitPlanned(page);
    await noTextOverflow(page, "review");
    await page.evaluate(() => localStorage.removeItem("placemaker.sections.v1"));
});

test("compact: the whole park design fits without scrolling; full-HD screenshots per stage", async ({ page }) => {
    await open(page, { width: 1920, height: 1080 });
    await page.evaluate(() => localStorage.removeItem("placemaker.sections.v1"));
    await shot(page, "r3-1-area-empty");
    await panelShot(page, "r3-panel-1-area-empty");
    await drawPolygon(page, FIELD);
    await shot(page, "r3-1-area");
    await panelShot(page, "r3-panel-1-area");
    await page.getByTestId("primary").click();
    await pickType(page, "parking");
    await waitPlanned(page);
    await shot(page, "r3-2-design-parking");
    await panelShot(page, "r3-panel-2-design-parking");
    await pickType(page, "park");
    await waitPlanned(page);
    // the picker is always open (user 2026-10-20: no collapse button for it); a type starts on its Standard style
    await expect(page.getByTestId("make-toggle")).toHaveCount(0);
    await expect(page.getByTestId("preset-bar-head")).toContainText("Standard");
    await shot(page, "r3-2-design-park");
    await panelShot(page, "r3-panel-2-design-park");
    await page.getByTestId("feature-toggle-playground").click();
    await page.getByTestId("feature-toggle-plaza").click();
    await waitPlanned(page);
    await panelShot(page, "r3-panel-2-design-park-features");
    await pickType(page, "plaza");
    await waitPlanned(page);
    await panelShot(page, "r3-panel-2-design-plaza");
    await pickType(page, "roads");
    await waitPlanned(page);
    await panelShot(page, "r3-panel-2-design-roads");
    await pickType(page, "park");
    await page.getByTestId("primary").click();
    await waitPlanned(page);
    await shot(page, "r3-3-review");
    await panelShot(page, "r3-panel-3-review");
    await page.evaluate(() => localStorage.removeItem("placemaker.sections.v1"));
});

test("places-inside slot is reserved under the type browser (hidden until round 3)", async ({ page }) => {
    await open(page);
    await toDesign(page, "park");
    await expect(page.getByTestId("places-inside")).toHaveCount(0);
    await page.evaluate(() => ((window as any).__pmPlacesInside = true));
    await pickType(page, "plaza");
    await expect(page.getByTestId("places-inside")).toBeVisible();
    const placesY = (await page.getByTestId("places-inside").boundingBox())!.y;
    expect(placesY).toBeGreaterThan((await page.getByTestId("recipes").boundingBox())!.y);
    expect(placesY).toBeLessThan((await page.getByTestId("options").boundingBox())!.y);
});

test("no tree editor, no recipe saving, no mix UI", async ({ page }) => {
    await open(page);
    await toDesign(page, "park");
    for (const id of ["add-layer", "layer-tree", "save-recipe", "delete-recipe", "mixes", "recipe-roads-with-parks", "recipe-park-plaza", "recipe-neighbourhood-park"])
        await expect(page.getByTestId(id)).toHaveCount(0);
});
