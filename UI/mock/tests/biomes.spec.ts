// P2-8 the Landscape card (UI/src/controls/BiomeMix.tsx) against the real Core park: pictures of the card, the bar drags
// like the zone tool's, a click on a part opens that biome's own settings, one click makes a biome 100 %.
import { expect, test } from "@playwright/test";
import { drawPolygon, FIELD, noTextOverflow, open, panelShot, pickType } from "./helpers";

async function waitCore(page: import("@playwright/test").Page) {
    await page.waitForFunction(() => {
        const b = (window as any).pm.backend;
        return !b.planning && !b.stale;
    });
}

async function park(page: import("@playwright/test").Page, lang?: string) {
    await open(page, { real: true, width: 1500, height: 1300, lang });
    await page.waitForFunction(() => (window as any).pm.backend.hostUp !== undefined);
    await drawPolygon(page, FIELD);
    await page.getByTestId("primary").click();
    await pickType(page, "park");
    await waitCore(page);
    const card = page.getByTestId("biomemix");
    await card.scrollIntoViewIfNeeded();
    return card;
}

test("landscape card: picture", async ({ page }) => {
    const card = await park(page);
    await expect(card).toBeVisible();
    await noTextOverflow(page, "landscape card");
    await panelShot(page, "biomes-01-card");
    await card.screenshot({ path: "mock/screenshots/biomes-01-card-only.png" });
});

test("landscape card: drag, click, add, only this", async ({ page }) => {
    const card = await park(page);
    // click the meadow: its box opens with its own settings
    await page.getByTestId("biomemix-part-biomeMeadow").click();
    await expect(page.getByTestId("biomemix-box-biomeMeadow")).toBeVisible();
    await expect(page.getByTestId("biomemix-box-biomeMeadow").getByTestId("control-biomeMeadowTrees")).toBeVisible();
    await panelShot(page, "biomes-02-meadow-box");
    await card.screenshot({ path: "mock/screenshots/biomes-02-card-only.png" });
    // drag the first line to the right: lawn grows, its neighbour shrinks
    const edge = page.getByTestId("biomemix-edge-0");
    const box = (await edge.boundingBox())!;
    await page.mouse.move(box.x + box.width / 2, box.y + box.height / 2);
    await page.mouse.down();
    await page.mouse.move(box.x + box.width / 2 + 40, box.y + box.height / 2, { steps: 5 });
    await page.mouse.up();
    await waitCore(page);
    // add the field, then make it the whole landscape
    await page.getByTestId("biomemix-add-toggle").click();
    await page.getByTestId("biomemix-add-biomeField").click();
    await waitCore(page);
    await expect(page.getByTestId("biomemix-box-biomeField")).toBeVisible();
    await page.getByTestId("biomemix-only").click();
    await waitCore(page);
    await expect(page.getByTestId("biomemix-box-share")).toHaveText("100%");
    await noTextOverflow(page, "landscape card field");
    await card.screenshot({ path: "mock/screenshots/biomes-03-field-only.png" });
    // the lawn box holds the park's own planting
    await page.getByTestId("biomemix-add-toggle").click();
    await page.getByTestId("biomemix-add-biomeLawn").click();
    await waitCore(page);
    await page.getByTestId("biomemix-part-biomeLawn").click();
    await expect(page.getByTestId("biomemix-box-biomeLawn").getByTestId("control-treeDensity")).toBeVisible();
    await card.screenshot({ path: "mock/screenshots/biomes-04-lawn-box.png" });
});
