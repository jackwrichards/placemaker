// Roads have no zoning (the user, 26 Oct: the Zoning category zones): no Zoning section in their parameters, for any style.
import { expect, test } from "@playwright/test";
import { drawPolygon, FIELD, open, panelShot, pickType, unfoldSections, waitPlanned } from "./helpers";

test("roads: no zoning in the parameters", async ({ page }) => {
    const errors = await open(page, { real: true });
    await drawPolygon(page, FIELD);
    await page.getByTestId("primary").click();
    await pickType(page, "roads");
    await waitPlanned(page);
    for (const style of ["standard", "terraced-hillside", "hill-town"]) {
        await page.evaluate((s) => (window as any).pm.trigger("Placemaker", "selectPreset", "roads", s), style);
        await waitPlanned(page);
        await unfoldSections(page);
        // the parameters (the mock's fake catalog still lists an edge-zoning add-on for roads; the game has none)
        const text = (await page.getByTestId("options").innerText()).split("ADD-ONS")[0];
        expect(text, style).not.toMatch(/\bZoning\b|No zoning/);
    }
    await panelShot(page, "roads-no-zoning");
    expect(errors).toEqual([]);
});
