import { expect, test } from "@playwright/test";
import { clickWorld, drawPolygon, FIELD, open, panelShot, shot, waitPlanned, pickType, unfoldSections } from "./helpers";

async function design(page: import("@playwright/test").Page, recipe: string, entrances = true) {
    await drawPolygon(page, FIELD);
    await page.getByTestId("primary").click();
    await pickType(page, recipe);
    await waitPlanned(page, entrances);
}

/** Unfold a control group of the root step (groups are folded by default, round 3). */
async function unfold(page: import("@playwright/test").Page, group: string) {
    const g = page.getByTestId("step-0").getByTestId("group-" + group).first();
    if ((await g.getAttribute("data-open")) !== "true") await g.getByTestId("group-" + group + "-toggle").click();
    await expect(g).toHaveAttribute("data-open", "true");
}

test("every control kind renders from the schema, grouped into foldable sections", async ({ page }) => {
    const errors = await open(page);
    await design(page, "parking");
    await unfold(page, "basics");
    await expect(page.getByTestId("control-angle")).toHaveAttribute("data-kind", "choice");
    await expect(page.getByTestId("control-surface")).toHaveAttribute("data-kind", "asset");
    await expect(page.getByTestId("control-greenery")).toHaveAttribute("data-kind", "toggle");
    // typed entrances replace the generic markers control
    await expect(page.getByTestId("control-entrances")).toHaveCount(0);
    await expect(page.getByTestId("entrances-0")).toBeVisible();
    // "more" tier lives in folded groups, essentials first; unfolding shows the rows
    await expect(page.getByTestId("control-aisle")).toHaveCount(0);
    for (const g of ["layout", "extras", "zoning"]) await unfold(page, g);
    await expect(page.getByTestId("control-aisle")).toHaveAttribute("data-kind", "slider");
    await expect(page.getByTestId("slider-aisle-value")).toHaveText("6.0 m");
    await expect(page.getByTestId("slider-chargers-value")).toHaveText("0");
    await expect(page.getByTestId("slider-fee-value")).toHaveText("¢5");
    await expect(page.getByTestId("group-basics")).toContainText("Basics");
    await expect(page.getByTestId("group-layout")).toContainText("Layout");
    await expect(page.getByTestId("group-zoning")).toContainText("Zoning");
    const groups = await page.locator('[data-testid^="group-"]').evaluateAll((els) => els.map((e) => e.getAttribute("data-testid")));
    expect(groups[0]).toBe("group-basics");
    await expect(page.getByTestId("more-options")).toHaveCount(0);
    await panelShot(page, "panel-parking-options");

    await pickType(page, "plaza");
    await unfoldSections(page);
    await expect(page.getByTestId("control-arrangement")).toHaveAttribute("data-kind", "arrangement");
    await pickType(page, "park");
    await unfold(page, "planting");
    await expect(page.getByTestId("control-treeSpecies")).toHaveAttribute("data-kind", "assetMulti");
    // untyped markers control still works as before (park entrances)
    await unfold(page, "paths");
    await expect(page.getByTestId("control-entrances")).toHaveAttribute("data-kind", "markers");
    expect(errors).toEqual([]);
});

test("visibleWhen, lock indicator, reset-to-default, randomize keeps locked values", async ({ page }) => {
    await open(page);
    await design(page, "park");
    await unfoldSections(page);
    // s15: a control that does not apply is shown greyed out with the reason, not hidden
    await expect(page.getByTestId("control-lakeSize")).toHaveAttribute("data-disabled", "true");
    await page.getByTestId("toggle-lake").click();
    await expect(page.getByTestId("control-lakeSize")).toBeVisible();
    await expect(page.getByTestId("control-lakeSize")).not.toHaveAttribute("data-disabled", "true");

    // no reset/lock until the player changes a value
    await expect(page.getByTestId("reset-pathWidth")).toHaveCount(0);
    await expect(page.getByTestId("lock-pathWidth")).toHaveCount(0);
    const track = page.getByTestId("slider-pathWidth").locator("div").first();
    await track.scrollIntoViewIfNeeded();
    const box = (await track.boundingBox())!;
    await page.mouse.click(box.x + box.width * 0.9, box.y + box.height / 2);
    await expect(page.getByTestId("slider-pathWidth-value")).toHaveText("7.5 m");
    await expect(page.getByTestId("lock-pathWidth")).toBeVisible();
    await expect(page.getByTestId("reset-pathWidth")).toBeVisible();
    await panelShot(page, "panel-lock-reset");

    // randomize twice: the locked value never moves
    await page.getByTestId("randomize").click();
    await page.getByTestId("randomize").click();
    await expect(page.getByTestId("slider-pathWidth-value")).toHaveText("7.5 m");

    await page.getByTestId("reset-pathWidth").click();
    await expect(page.getByTestId("slider-pathWidth-value")).toHaveText("4.0 m");
    await expect(page.getByTestId("lock-pathWidth")).toHaveCount(0);
});

test("asset picker: search, thumbnails, automatic; closes with X, Esc and a click outside", async ({ page }) => {
    await open(page);
    await design(page, "parking");
    await unfoldSections(page);
    const picker = page.getByTestId("asset-picker");
    await page.getByTestId("asset-button-surface").click();
    await expect(picker).toBeVisible();
    await expect(page.getByTestId("asset-automatic")).toBeVisible();
    await expect(page.getByTestId("asset-concrete")).toBeVisible();
    // beside the panel, not over it
    const pb = (await page.getByTestId("placemaker-panel").boundingBox())!;
    const fb = (await picker.boundingBox())!;
    expect(fb.x + fb.width).toBeLessThanOrEqual(pb.x + 1);
    await page.getByTestId("asset-search").fill("conc");
    await expect(page.getByTestId("asset-asphalt")).toHaveCount(0);
    await page.getByTestId("asset-concrete").click();
    await expect(picker).toHaveCount(0);
    await expect(page.getByTestId("asset-button-surface")).toContainText("Concrete");

    // X
    await page.getByTestId("asset-button-surface").click();
    await expect(picker).toContainText("Recent");
    await expect(page.getByTestId("asset-picker-close").locator("img")).toHaveCount(1);
    await panelShot(page, "panel-asset-picker");
    await page.getByTestId("asset-picker-close").click();
    await expect(picker).toHaveCount(0);
    expect(await page.evaluate(() => (window as any).pm.backend.open)).toBe(true);

    // Esc closes the picker only, not the tool
    await page.getByTestId("asset-button-surface").click();
    await expect(picker).toBeVisible();
    await page.keyboard.press("Escape");
    await expect(picker).toHaveCount(0);
    expect(await page.evaluate(() => (window as any).pm.backend.open)).toBe(true);

    // click outside (on the map)
    await page.getByTestId("asset-button-surface").click();
    await expect(picker).toBeVisible();
    await page.mouse.click(300, 600);
    await expect(picker).toHaveCount(0);
    expect(await page.evaluate(() => (window as any).pm.backend.ring.length)).toBe(4);

    await page.getByTestId("asset-button-surface").click();
    await page.getByTestId("asset-automatic").click();
    await expect(page.getByTestId("asset-button-surface")).toContainText("Automatic");

    await pickType(page, "park");
    await unfold(page, "planting");
    await page.getByTestId("assetmulti-add-treeSpecies").click();
    await page.getByTestId("asset-linden").click();
    await page.getByTestId("asset-ginkgo").click();
    await page.getByTestId("asset-picker-done").click();
    await expect(page.getByTestId("chip-linden")).toBeVisible();
    await expect(page.getByTestId("chip-ginkgo")).toBeVisible();
    await page.getByTestId("chip-remove-linden").click();
    await expect(page.getByTestId("chip-linden")).toHaveCount(0);

    await pickType(page, "plaza");
    await unfoldSections(page);
    const arr = page.getByTestId("arrangement-arrangement");
    await expect(arr).toContainText("Wooden Bench");
    await page.getByTestId("arrangement-add-arrangement").click();
    await page.getByTestId("asset-bollard").click();
    await expect(arr).toContainText("Bollard");
    await page.getByTestId("arr-remove-0").click();
    await panelShot(page, "panel-arrangement");
});

test("entrances: none placed automatically; disabled primary leads to the Entrances section and starts placement; typed list with remove", async ({ page }) => {
    const errors = await open(page);
    const primary = page.getByTestId("primary");
    await design(page, "parking", false);
    // FEEDBACK-4b: no suggested entrance is placed for the player
    await expect(page.getByTestId("marker-list")).toHaveCount(0);
    expect(await page.evaluate(() => (window as any).pm.backend.root.markers.length)).toBe(0);
    await expect(page.getByTestId("entrance-required")).toContainText("Add one to build");
    await expect(primary).toHaveAttribute("aria-disabled", "true");
    await expect(primary).toHaveAttribute("data-action", "placeMarkers");
    await expect(page.getByTestId("primary-reason")).toContainText("Add an entrance");
    await expect(primary).toHaveAttribute("data-lead", "true");   // (Phase 1) yellow, plainly clickable
    // five typed buttons, each with an icon
    for (const k of ["entrance", "entry", "exit", "alley", "footpath"]) await expect(page.getByTestId(`marker-kind-${k}`).locator("img")).toHaveCount(1);
    await panelShot(page, "panel-entrance-required");

    // scroll the panel to the bottom, then press the disabled button anyway: the Entrances block scrolls into
    // view, flashes, and placement starts with its first type
    await page.getByTestId("panel-body").evaluate((el) => {
        for (let e: HTMLElement | null = el; e; e = e.firstElementChild as HTMLElement | null) e.scrollTop = 100000;
    });
    await primary.click({ force: true }); // aria-disabled: Playwright would refuse a normal click
    await expect(page.getByTestId("marker-kind-entrance")).toHaveAttribute("aria-pressed", "true");
    expect(await page.evaluate(() => (window as any).pm.backend.markerMode)).toEqual({ stepPath: "0", kind: "entrance" });
    await expect(page.getByTestId("entrances-wrap-0")).toHaveAttribute("data-flash", "true");
    await expect(page.getByTestId("marker-kind-entrance")).toBeInViewport();
    await panelShot(page, "panel-entrance-lead");
    await expect(page.getByTestId("entrances-wrap-0")).toHaveAttribute("data-flash", "false", { timeout: 4000 });
    expect(await page.evaluate(() => (window as any).pm.backend.stage)).toBe("design");

    // place a two-way entrance, show it, remove it again
    await clickWorld(page, (FIELD[0][0] + FIELD[1][0]) / 2, (FIELD[0][1] + FIELD[1][1]) / 2);
    await expect(page.getByTestId("marker-0")).toHaveAttribute("data-kind", "entrance");
    await expect(page.getByTestId("marker-0")).toContainText("2-way 1");
    await page.getByTestId("marker-kind-entrance").click();   // the lit type again: placing stops
    await expect(page.getByTestId("entrance-required")).toHaveCount(0);
    await expect(primary).toHaveAttribute("aria-disabled", "false");
    await panelShot(page, "panel-entrances");
    await page.getByTestId("marker-show-0").click();
    expect(await page.evaluate(() => (window as any).pm.backend.locateAt)).not.toBeNull();
    await page.getByTestId("marker-remove-0").click();
    await expect(page.getByTestId("entrance-required")).toContainText("Add one to build");
    await expect(primary).toHaveAttribute("aria-disabled", "true");

    // choose "Exit only", click the outline → placed with its type; Build path unblocked
    await page.getByTestId("marker-kind-exit").click();
    await expect(page.getByTestId("marker-kind-exit")).toHaveAttribute("aria-pressed", "true");
    await shot(page, "14-placing-entrance");
    await clickWorld(page, (FIELD[1][0] + FIELD[2][0]) / 2, (FIELD[1][1] + FIELD[2][1]) / 2);
    await expect(page.getByTestId("marker-0")).toHaveAttribute("data-kind", "exit");
    // switch type while placing
    await page.getByTestId("marker-kind-alley").click();
    await clickWorld(page, (FIELD[2][0] + FIELD[3][0]) / 2, (FIELD[2][1] + FIELD[3][1]) / 2);
    await expect(page.getByTestId("marker-1")).toHaveAttribute("data-kind", "alley");
    await page.getByTestId("marker-kind-alley").click();
    await expect(page.getByTestId("marker-kind-alley")).toHaveAttribute("aria-pressed", "false");
    await expect(page.getByTestId("entrance-required")).toHaveCount(0);
    await expect(primary).toHaveAttribute("aria-disabled", "false");
    await expect(primary).toHaveAttribute("data-action", "toReview");
    await panelShot(page, "panel-markers");
    expect(errors).toEqual([]);
});

test("footpath entrances come with or without a crosswalk; car entrances never offer one", async ({ page }) => {
    const errors = await open(page);
    await design(page, "parking", false);
    const toggle = page.getByTestId("marker-crosswalk");
    await expect(toggle).toBeVisible();
    await expect(toggle).toHaveAttribute("aria-checked", "false");
    // a footpath placed with the switch on is stored as "footpath+crosswalk"
    await page.getByTestId("marker-kind-footpath").click();
    await toggle.click();
    await expect(toggle).toHaveAttribute("aria-checked", "true");
    await clickWorld(page, (FIELD[0][0] + FIELD[1][0]) / 2, (FIELD[0][1] + FIELD[1][1]) / 2);
    await expect(page.getByTestId("marker-0")).toHaveAttribute("data-kind", "footpath");
    await expect(page.getByTestId("marker-crosswalk-0")).toHaveAttribute("aria-checked", "true");
    expect(await page.evaluate(() => (window as any).pm.backend.root.markers[0].kind)).toBe("footpath+crosswalk");
    await panelShot(page, "panel-entrance-crosswalk");
    // switched off on the placed one
    await page.getByTestId("marker-crosswalk-0").click();
    await expect(page.getByTestId("marker-crosswalk-0")).toHaveAttribute("aria-checked", "false");
    expect(await page.evaluate(() => (window as any).pm.backend.root.markers[0].kind)).toBe("footpath");
    // a two-way car entrance, switch still on: no crosswalk, no crosswalk button
    await page.getByTestId("marker-kind-entrance").click();
    await clickWorld(page, (FIELD[1][0] + FIELD[2][0]) / 2, (FIELD[1][1] + FIELD[2][1]) / 2);
    await expect(page.getByTestId("marker-1")).toHaveAttribute("data-kind", "entrance");
    await expect(page.getByTestId("marker-crosswalk-1")).toHaveCount(0);
    expect(await page.evaluate(() => (window as any).pm.backend.root.markers[1].kind)).toBe("entrance");
    expect(errors).toEqual([]);
});

test("disabled primary explains why; permanent effects need acknowledgement", async ({ page }) => {
    await open(page);
    const primary = page.getByTestId("primary");
    await expect(primary).toHaveAttribute("aria-disabled", "true");
    await expect(page.getByTestId("primary-reason")).toContainText("Select an area first");
    await primary.hover();
    await expect(page.getByTestId("tooltip")).toContainText("Select an area first");
    await panelShot(page, "panel-disabled-reason");

    // a pond is permanent: Build waits for the tick (and tells C#)
    await design(page, "park");
    await page.getByTestId("preset-pond").click();
    await primary.click();
    await waitPlanned(page);
    await expect(page.getByTestId("permanent").first()).toBeVisible();
    await expect(primary).toHaveAttribute("aria-disabled", "true");
    await expect(page.getByTestId("primary-reason")).toContainText("Click OK to build");
    await panelShot(page, "panel-permanent");
    await page.getByTestId("ack-permanent").click();
    expect(await page.evaluate(() => (window as any).pm.backend.ackPermanent)).toBe(true);
    await expect(primary).toHaveAttribute("aria-disabled", "false");
});

test("not enough money: cost turns red, Build is disabled with the reason", async ({ page }) => {
    await open(page);
    await page.evaluate(() => {
        const b = (window as any).pm.backend;
        b.money = 1000;
    });
    await drawPolygon(page, FIELD);
    await page.getByTestId("primary").click();
    await page.getByTestId("primary").click();
    await waitPlanned(page);
    await expect(page.getByTestId("cant-afford")).toBeVisible();
    await expect(page.getByTestId("primary")).toHaveAttribute("aria-disabled", "true");
    await expect(page.getByTestId("primary-reason")).toContainText("Not enough money");
});
