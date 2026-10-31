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
