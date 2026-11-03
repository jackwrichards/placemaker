// Against the REAL Core layers served by the mock host (whatever GeneratorRegistry.All returns).
// Skips itself when the host has no layers, so the suite stays green on a branch without them.
import { expect, test } from "@playwright/test";
import { clickWorld, drawPolygon, FIELD, noTextOverflow, open, panelShot, shot, waitPlanned, pickType } from "./helpers";

async function realLayers(page: import("@playwright/test").Page) {
    await page.waitForFunction(() => (window as any).pm.backend.hostUp !== undefined);
    await page.waitForTimeout(300);
    return page.evaluate(() => Array.from((window as any).pm.backend.hostLayers as Set<string>));
}

/** Shows every type in the grid browser (the "All" category chip). */
async function openAllCategories(page: import("@playwright/test").Page) {
    const all = page.getByTestId("make-group-all");
    if (await all.count()) await all.click();
}

/**
 * (s15) Real park-family layers need a placed entrance before Review (s9: none are placed automatically). The
 * disabled primary button then starts placement (Playwright needs force for aria-disabled): place one on the
 * outline and finish. This is what failed on master since s9 ("Next disabled at Design").
 */
async function placeEntranceIfAsked(page: import("@playwright/test").Page) {
    const primary = page.getByTestId("primary");
    if ((await primary.getAttribute("data-action")) === "placeMarkers") await primary.click({ force: true });
    else {
        // The real park's untyped entrance markers: start placement on the step's markers control.
        const kind = await page.evaluate(() => {
            const b = (window as any).pm.backend;
            const p = b.primary();
            if (p.enabled || !b.root) return null;
            const c = b.layer(b.root.layer)?.controls?.find((x: any) => x.kind === "markers");
            return c ? (c.markerKinds && c.markerKinds[0]) || c.id : null;
        });
        if (!kind) return;
        await page.evaluate((k) => (window as any).pm.trigger("Placemaker", "startMarkers", "0", k), kind);
    }
    await page.getByTestId("marker-kinds").waitFor();
    await clickWorld(page, (FIELD[0][0] + FIELD[1][0]) / 2, (FIELD[0][1] + FIELD[1][1]) / 2);
    // stop placing (the lit type button again; no banner any more)
    await page.evaluate(() => (window as any).pm.trigger("Placemaker", "endMarkers"));
    await waitCore(page);
}

async function waitCore(page: import("@playwright/test").Page) {
    await waitPlanned(page);
    await page.waitForFunction(() => (window as any).pm.backend.plan?.source === "core");
}

test("real Core: shipped recipes in the Make row, real plans on the map", async ({ page }) => {
    const errors = await open(page, { real: true });
    const layers = await realLayers(page);
    test.skip(!layers.includes("park"), "mock host has no real park layer on this branch");

    await drawPolygon(page, FIELD);
    await page.getByTestId("primary").click();
    await openAllCategories(page);
    for (const id of ["park", "plaza", "campground"])
        if (await page.evaluate((r) => (window as any).pm.backend.recipes.some((x: any) => x.info.id === r), id)) {
            await pickType(page, id);
            await expect(page.getByTestId(`recipe-${id}`)).toHaveAttribute("aria-pressed", "true");
        }

    await pickType(page, "park");
    await waitCore(page);
    await page.getByTestId("browser-search-toggle").click();
    await page.getByTestId("browser-search").fill("garden");
    await noTextOverflow(page, "real browser search");
    await panelShot(page, "real-panel-browser-search");
    await page.getByTestId("browser-clear").click();
    // Core features: the park offers a central plaza with its shape / position / size under the toggle
    if (await page.getByTestId("feature-toggle-plaza").count()) {
        await page.getByTestId("feature-toggle-plaza").click();
        await waitCore(page);
        if ((await page.getByTestId("feature-plaza").getAttribute("data-open")) !== "true") await page.getByTestId("feature-fold-plaza").click();
        await expect(page.getByTestId("feature-options-plaza").getByTestId("control-plazaShape")).toBeVisible();
        await expect(page.getByTestId("step-0").getByTestId("group-plaza")).toHaveCount(0);
    }
    await noTextOverflow(page, "real park design");
    await shot(page, "real-01-park-design");
    await panelShot(page, "real-panel-park-design");

    await placeEntranceIfAsked(page);
    await page.getByTestId("primary").click();
    await waitCore(page);
    await expect(page.getByTestId("review-cost")).toContainText("¢");
    await shot(page, "real-02-park-review");
    await panelShot(page, "real-panel-review");

    await page.getByTestId("stagebar-design").click();
    await openAllCategories(page);
    if (await page.getByTestId("recipe-split-park-plaza").count()) {
        await pickType(page, "split-park-plaza");
        await waitCore(page);
        await shot(page, "real-03-split-park-plaza");
        await panelShot(page, "real-panel-split");
    }
    if (await page.getByTestId("recipe-plaza").count()) {
        await pickType(page, "plaza");
        await waitCore(page);
        await shot(page, "real-04-plaza");
    }
    expect(errors).toEqual([]);
});
