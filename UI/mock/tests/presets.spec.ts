// Playtest 3: one browser Category ▸ Type ▸ Preset, search, keyboard; active-preset bar with
// Save (over a built-in = "edited", Reset to default), Save as (★ own preset), Rename, Delete, Export.
import { expect, Page, test } from "@playwright/test";
import { drawPolygon, FIELD, noTextOverflow, open, panelShot, waitPlanned, pickType } from "./helpers";

async function toDesign(page: Page, recipe = "park") {
    await drawPolygon(page, FIELD);
    await page.getByTestId("primary").click();
    await pickType(page, recipe);
    await waitPlanned(page);
}

const backend = (page: Page, fn: string) => page.evaluate((f) => new Function("b", "return " + f)((window as any).pm.backend), fn);

test("browser: categories ▸ types ▸ presets; clicking a preset applies it; the selected type lists its presets", async ({ page }) => {
    const errors = await open(page);
    await toDesign(page, "park");
    const park = page.getByTestId("presets-park");
    // selected type expanded: Standard (its own design, a style like the others) + its built-ins; other types collapsed
    await expect(park.getByTestId("preset-standard")).toHaveAttribute("aria-pressed", "true");
    for (const id of ["formal", "wild", "pond"]) await expect(park.getByTestId(`preset-${id}`)).toBeVisible();
    await expect(page.getByTestId("presets-plaza")).toHaveCount(0);
    await expect(page.getByTestId("preset-bar-name")).toHaveText("Standard");
    // compact chips and tiles
    const h = (await park.getByTestId("preset-formal").boundingBox())!.height;
    expect(h).toBeLessThanOrEqual(24);
    expect((await page.getByTestId("recipe-park").boundingBox())!.height).toBeLessThanOrEqual(30);

    await park.getByTestId("preset-wild").click();
    await expect(park.getByTestId("preset-wild")).toHaveAttribute("aria-pressed", "true");
    await expect(park.getByTestId("preset-standard")).toHaveAttribute("aria-pressed", "false");
    expect(await backend(page, "b.root.values.style")).toBe("meadow");
    await expect(page.getByTestId("preset-bar-name")).toHaveText("Wild meadow");
    await expect(page.getByTestId("preset-modified")).toHaveCount(0);
    await expect(page.getByTestId("preset-save")).toHaveCount(0);

    // Standard is a style like the others: picked again any time (it was "Default", which could not be reselected)
    await park.getByTestId("preset-standard").click();
    await expect(park.getByTestId("preset-standard")).toHaveAttribute("aria-pressed", "true");
    await expect(park.getByTestId("preset-wild")).toHaveAttribute("aria-pressed", "false");
    await expect(page.getByTestId("preset-bar-name")).toHaveText("Standard");
    expect(await backend(page, "b.active.id")).toBe("standard");
    await expect(page.getByTestId("preset-modified")).toHaveCount(0);
    await park.getByTestId("preset-wild").click();
    await expect(park.getByTestId("preset-wild")).toHaveAttribute("aria-pressed", "true");

    // another type: selected with its defaults, expanded instead
    await pickType(page, "plaza");
    await expect(page.getByTestId("recipe-plaza")).toHaveAttribute("aria-pressed", "true");
    await expect(page.getByTestId("presets-plaza").getByTestId("preset-civic")).toBeVisible();
    await expect(page.getByTestId("presets-park")).toHaveCount(0);
    await page.getByTestId("presets-plaza").getByTestId("preset-civic").click();
    expect(await backend(page, "b.root.values.pattern")).toBe("radial");
    expect(await backend(page, "b.active.id")).toBe("civic");
    // undo goes back to the plain plaza
    await page.keyboard.press("Control+z");
    await expect(page.getByTestId("presets-plaza").getByTestId("preset-standard")).toHaveAttribute("aria-pressed", "true");

    // a category chip lists its own types
    await page.getByTestId("make-group-shops").click();
    await expect(page.getByTestId("recipe-park")).toHaveCount(0);
    await page.getByTestId("make-group-parks").click();
    await expect(page.getByTestId("recipe-park")).toBeVisible();
    await noTextOverflow(page, "browser");
    await panelShot(page, "p-browser");
    expect(errors).toEqual([]);
});

test("browser: search filters categories, types and presets; keyboard moves between rows", async ({ page }) => {
    await open(page);
    await toDesign(page, "park");
    const search = page.getByTestId("browser-search");
    await page.getByTestId("browser-search-toggle").click();
    await search.fill("civ");
    await expect(page.getByTestId("make-group-shops")).toBeVisible();
    await expect(page.getByTestId("make-group-parks")).toHaveCount(0);
    await expect(page.getByTestId("preset-civic")).toBeVisible();
    await expect(page.getByTestId("preset-market")).toHaveCount(0);
    await panelShot(page, "p-search");
    // a category name matches → all its types
    await search.fill("parks");
    await expect(page.getByTestId("recipe-park")).toBeVisible();
    await expect(page.getByTestId("recipe-plaza")).toHaveCount(0);
    // nothing
    await search.fill("zzz");
    await expect(page.getByTestId("browser-empty")).toBeVisible();
    // Esc clears and closes the search (and does not close the tool)
    await search.press("Escape");
    await expect(search).toHaveCount(0);
    await expect(page.getByTestId("placemaker-panel")).toBeVisible();
    await expect(page.getByTestId("recipe-park")).toBeVisible();
    // Enter picks the first match
    await page.getByTestId("browser-search-toggle").click();
    await search.fill("meadow");
    await search.press("Enter");
    await expect.poll(() => backend(page, "b.active && b.active.id")).toBe("wild");
    await page.getByTestId("browser-clear").click();
    await expect(search).toHaveCount(0);

    await page.getByTestId("browser-search-toggle").click();
    // ↓ from the search box enters the grid; arrows walk it; Enter activates the focused item, not the
    // tool's primary action
    await search.focus();
    await search.press("ArrowDown"); // first category chip
    await expect(page.getByTestId("make-group-roads")).toBeFocused();
    await page.keyboard.press("ArrowRight");
    await expect(page.getByTestId("make-group-parking")).toBeFocused();
    await page.keyboard.press("ArrowLeft");
    await expect(page.getByTestId("make-group-roads")).toBeFocused();
    await page.keyboard.press("ArrowDown"); // the row below: wrapped categories in a narrow panel, else the types
    if (await page.locator('[data-nav="cat"]:focus').count()) await page.keyboard.press("ArrowDown");
    await expect(page.locator('[data-testid="recipes"] [data-nav="type"]:focus')).toHaveCount(1);
    await page.getByTestId("preset-standard").focus();
    await page.keyboard.press("ArrowRight"); // Formal garden
    await expect(page.getByTestId("preset-formal")).toBeFocused();
    await page.keyboard.press("Enter");
    await expect.poll(() => backend(page, "b.active.id")).toBe("formal");
    expect(await backend(page, "b.stage")).toBe("design");
    await page.keyboard.press("ArrowUp"); // back up into the tiles
    await expect(page.locator('[data-testid="recipes"] [data-nav="type"]:focus')).toHaveCount(1);
});

test("modify a built-in → modified → Save over it (edited) → Reset to default", async ({ page }) => {
    const errors = await open(page);
    await toDesign(page, "park");
    const park = page.getByTestId("presets-park");
    await park.getByTestId("preset-formal").click();
    await expect(page.getByTestId("preset-bar")).toHaveAttribute("data-modified", "false");
    await page.getByTestId("feature-toggle-playground").click();
    await expect(page.getByTestId("preset-modified")).toBeVisible();
    await expect(page.getByTestId("preset-bar")).toHaveAttribute("data-modified", "true");
    await expect(page.getByTestId("preset-save")).toHaveAttribute("aria-disabled", "false");
    await panelShot(page, "p-modified");
    await page.getByTestId("preset-save").click();
    await expect(page.getByTestId("preset-modified")).toHaveCount(0);
    await expect(park.getByTestId("preset-edited-formal")).toBeVisible();
    // the saved version is what the preset applies now
    await park.getByTestId("preset-wild").click();
    await expect(page.getByTestId("feature-playground")).toHaveAttribute("data-enabled", "false");
    await park.getByTestId("preset-formal").click();
    await expect(page.getByTestId("feature-playground")).toHaveAttribute("data-enabled", "true");
    await expect(page.getByTestId("preset-modified")).toHaveCount(0);

    // ⋯ → Reset to default (confirmed) — built-ins can't be renamed or deleted
    await page.getByTestId("preset-more").click();
    await expect(page.getByTestId("preset-rename")).toHaveCount(0);
    await expect(page.getByTestId("preset-delete")).toHaveCount(0);
    await page.getByTestId("preset-reset").click();
    await expect(page.getByTestId("confirm-dialog")).toBeVisible();
    await page.getByTestId("confirm-ok").click();
    await expect(park.getByTestId("preset-edited-formal")).toHaveCount(0);
    await expect(page.getByTestId("feature-playground")).toHaveAttribute("data-enabled", "false");
    await expect(page.getByTestId("preset-modified")).toHaveCount(0);
    expect(errors).toEqual([]);
});

test("Save as → own preset (★, active) → save changes → rename → delete; export shows the file", async ({ page }) => {
    const errors = await open(page);
    await toDesign(page, "park");
    const park = page.getByTestId("presets-park");
    await park.getByTestId("preset-formal").click();
    await page.getByTestId("feature-toggle-playground").click();
    await page.getByTestId("preset-save-as").click();
    const input = page.getByTestId("preset-name-input");
    await expect(input).toHaveValue("Formal garden (mine)");
    await input.fill("");
    await expect(page.getByTestId("preset-name-save")).toHaveAttribute("aria-disabled", "true");
    await input.fill("Lawn with playground");
    await panelShot(page, "p-save-as");
    await input.press("Enter");
    const own = park.locator('[data-testid^="preset-custom-"]');
    await expect(own).toHaveCount(1);
    await expect(own).toContainText("Lawn with playground");
    await expect(own).toHaveAttribute("aria-pressed", "true");
    await expect(own.locator('img[data-icon="star"]')).toHaveCount(1);
    await expect(page.getByTestId("preset-bar-name")).toHaveText("Lawn with playground");
    await expect(page.getByTestId("preset-modified")).toHaveCount(0);
    // the built-in it came from is untouched
    await expect(park.getByTestId("preset-edited-formal")).toHaveCount(0);
    // the stage's Enter didn't fire while typing
    expect(await backend(page, "b.stage")).toBe("design");

    // change it, Save overwrites it
    await page.getByTestId("feature-toggle-picnic").click();
    await expect(page.getByTestId("preset-modified")).toBeVisible();
    await page.getByTestId("preset-save").click();
    await expect(page.getByTestId("preset-modified")).toHaveCount(0);
    await park.getByTestId("preset-wild").click();
    await own.click();
    await expect(page.getByTestId("feature-picnic")).toHaveAttribute("data-enabled", "true");
    await expect(page.getByTestId("feature-playground")).toHaveAttribute("data-enabled", "true");

    // rename
    await page.getByTestId("preset-more").click();
    await panelShot(page, "p-menu");
    await page.getByTestId("preset-rename").click();
    await page.getByTestId("preset-name-input").fill("Family lawn");
    await page.getByTestId("preset-name-save").click();
    await expect(own).toContainText("Family lawn");
    await expect(page.getByTestId("preset-bar-name")).toHaveText("Family lawn");

    // export: shows where presets.json is, with the note
    await page.getByTestId("preset-more").click();
    await page.getByTestId("preset-export").click();
    await expect(page.getByTestId("presets-file")).toContainText("Send this file to share your styles");
    await expect(page.getByTestId("presets-file-path")).toContainText("ModsData\\Placemaker\\presets.json");
    await noTextOverflow(page, "export note");
    await panelShot(page, "p-export");

    // delete: cancel keeps it, confirm removes it
    await page.getByTestId("preset-more").click();
    await page.getByTestId("preset-delete").click();
    await page.getByTestId("confirm-cancel").click();
    await expect(own).toHaveCount(1);
    await page.getByTestId("preset-more").click();
    await page.getByTestId("preset-delete").click();
    await page.getByTestId("confirm-ok").click();
    await expect(own).toHaveCount(0);
    await expect(page.getByTestId("preset-bar-name")).toHaveText("Standard");
    // the settings stay (they now differ from the type's defaults)
    await expect(page.getByTestId("preset-modified")).toBeVisible();
    expect(errors).toEqual([]);
});

test("long own preset names and many types never overflow", async ({ page }) => {
    await open(page);
    await toDesign(page, "park");
    await page.evaluate(() => (window as any).pm.backend.addMoreTypes());
    await page.getByTestId("preset-save-as").click();
    await page.getByTestId("preset-name-input").fill("A really very long preset name for the big park");
    await page.getByTestId("preset-name-save").click();
    await expect(page.getByTestId("presets-park").locator('[data-testid^="preset-custom-"]')).toHaveCount(1);
    for (const g of ["shops", "parking", "roads", "nature", "parks"]) {
        if (!(await page.getByTestId(`make-group-${g}`).count())) await page.getByTestId("make-groups-more").click();
        await page.getByTestId(`make-group-${g}`).click();
        await noTextOverflow(page, "long names " + g);
    }
    const panel = (await page.getByTestId("placemaker-panel").boundingBox())!;
    const bar = (await page.getByTestId("preset-bar").boundingBox())!;
    expect(bar.x + bar.width).toBeLessThanOrEqual(panel.x + panel.width + 1);
    await panelShot(page, "p-long-names");
});
