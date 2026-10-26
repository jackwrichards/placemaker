// Style previews: the chosen style's example photo on top of the style card, three views, the view kept across styles.
import { expect, test } from "@playwright/test";
import { drawPolygon, FIELD, open, panelShot, pickType, waitPlanned } from "./helpers";

test("style preview: photo on top, views switch and stay", async ({ page }) => {
    const errors = await open(page, { real: true });
    await page.evaluate(() => localStorage.removeItem("placemaker.previewView.v2"));
    await drawPolygon(page, FIELD);
    await page.getByTestId("primary").click();
    await pickType(page, "park");
    await waitPlanned(page);
    await page.evaluate(() => (window as any).pm.trigger("Placemaker", "selectPreset", "park", "natural"));
    const preview = page.getByTestId("style-preview");
    await expect(preview).toBeVisible({ timeout: 10000 });
    await expect(preview).toHaveAttribute("data-view", "iso");
    await panelShot(page, "style-preview-iso");
    await page.getByTestId("style-preview-close").click();
    await expect(preview).toHaveAttribute("data-view", "close");
    await panelShot(page, "style-preview-close");
    // another type with a photo keeps the close view
    await page.getByTestId("primary").count();
    await pickType(page, "adventurePlayground");
    await waitPlanned(page);
    await page.evaluate(() => (window as any).pm.trigger("Placemaker", "selectPreset", "adventurePlayground", "adventure"));
    await expect(page.getByTestId("style-preview")).toHaveAttribute("data-view", "close", { timeout: 10000 });
    expect(errors).toEqual([]);
});
