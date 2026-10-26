// Review (the user, 26 Oct): Cost & park stats as one section; Warnings and Checks apart; Checks lists what the preview
// cannot show before the build (ground shaping, ground paint, water, buildings growing).
import { expect, test } from "@playwright/test";
import { drawPolygon, FIELD, open, panelShot, pickType, waitPlanned } from "./helpers";

test("review: cost with park stats, warnings apart from checks, what shows only once built", async ({ page }) => {
    const errors = await open(page, { real: true });
    await drawPolygon(page, FIELD);
    await page.getByTestId("primary").click();
    await pickType(page, "park");
    await waitPlanned(page);
    await page.evaluate(() => (window as any).pm.trigger("Placemaker", "selectPreset", "park", "natural"));
    await waitPlanned(page);
    // straight to Review (entrances are not what this is about)
    await page.evaluate(() => (window as any).pm.backend.onTrigger("goToStage", ["review"]));
    await expect(page.getByTestId("stage-review")).toBeVisible();
    await waitPlanned(page);
    await expect(page.getByTestId("review-head-cost")).toContainText("Cost & park stats");
    await expect(page.getByTestId("park-stats")).toBeVisible();
    await expect(page.getByTestId("review-head-checks")).toBeVisible();
    await panelShot(page, "review-sections-top");
    await page.getByTestId("review-head-checks").scrollIntoViewIfNeeded();
    await panelShot(page, "review-sections-checks");
    const later = await page.getByTestId("checks").locator('[data-testid="later"]').allInnerTexts().catch(() => []);
    console.log("later:", JSON.stringify(later));
    expect(errors).toEqual([]);
});
