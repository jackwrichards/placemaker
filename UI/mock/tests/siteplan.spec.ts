// Sketch study: the top view (SitePlanCard) on the real Core host, for odd outlines (a T, a triangle, a circle, a field):
// Design shows it beside the panel, Review in the panel (beside its legend), a hovered row blinks, the expand button opens the large view. Screenshots land in
// mock/screenshots/siteplan-*.png for review.
import { expect, Page, test } from "@playwright/test";
import { clickWorld, drawPolygon, FIELD, open, panelShot, pickType, shot, waitPlanned } from "./helpers";

const T: [number, number][] = [[600, 300], [660, 300], [660, 400], [740, 400], [740, 470], [520, 470], [520, 400], [600, 400]];
const TRI: [number, number][] = [[530, 290], [790, 300], [640, 490]];
const CIRCLE: [number, number][] = Array.from({ length: 28 }, (_, i) => {
    const a = (i / 28) * Math.PI * 2;
    return [660 + Math.cos(a) * 95, 385 + Math.sin(a) * 95] as [number, number];
});

/** One entrance on the outline's first edge (the real types need one before Review). */
async function entrance(page: Page, ring: [number, number][]) {
    const kind = await page.evaluate(() => {
        const b = (window as any).pm.backend;
        const c = b.layer(b.root.layer)?.controls?.find((x: any) => x.kind === "markers");
        return c ? (c.markerKinds && c.markerKinds[0]) || (c.options && c.options[0]) || c.id : null;
    });
    if (!kind) return;
    await page.evaluate((k) => (window as any).pm.trigger("Placemaker", "startMarkers", "0", k), kind);
    await page.getByTestId("marker-kinds").waitFor();
    await clickWorld(page, (ring[0][0] + ring[1][0]) / 2, (ring[0][1] + ring[1][1]) / 2);
    await page.evaluate(() => (window as any).pm.trigger("Placemaker", "endMarkers"));
    await waitPlanned(page);
}

async function design(page: Page, ring: [number, number][], type: string) {
    await drawPolygon(page, ring);
    await page.getByTestId("primary").click();
    await pickType(page, type);
    await waitPlanned(page);
}

const cases: { name: string; ring: [number, number][]; type: string }[] = [
    { name: "parking-field", ring: FIELD, type: "parking" },
    { name: "park-T", ring: T, type: "park" },
    { name: "parking-triangle", ring: TRI, type: "parking" },
    { name: "park-circle", ring: CIRCLE, type: "park" },
    { name: "mall-field", ring: FIELD, type: "stripMall" },
];

for (const c of cases) {
    test("top view: " + c.name, async ({ page }) => {
        const errors = await open(page, { real: true });
        await design(page, c.ring, c.type);
        const win = page.getByTestId("siteplan-window");
        await expect(win).toBeVisible({ timeout: 15000 });
        await expect(win.getByTestId("siteplan-picture").locator("svg")).toHaveCount(1);
        await shot(page, "siteplan-" + c.name + "-design");

        // Review: the card in the panel, beside its legend
        await entrance(page, c.ring);
        await page.getByTestId("primary").click();
        const card = page.getByTestId("placemaker-panel").getByTestId("siteplan-card");
        await expect(card).toBeVisible({ timeout: 15000 });
        await panelShot(page, "siteplan-" + c.name + "-review");

        // a row hovered: only that thing; then the large view
        const rows = card.getByTestId("siteplan-rows").locator("[data-testid^='siteplan-row-']");
        // a hovered row blinks (nothing else is hidden)
        if (await rows.count()) {
            await rows.first().hover();
            await page.waitForTimeout(300);
            const box = await page.getByTestId("placemaker-panel").boundingBox();
            await page.screenshot({ path: "mock/screenshots/siteplan-" + c.name + "-hover.png", clip: { x: box!.x - 10, y: box!.y, width: box!.width + 20, height: Math.min(box!.height, 600) } });
        }
        await card.getByTestId("siteplan-expand").click();
        await expect(page.getByTestId("siteplan-flyout")).toBeVisible();
        await page.mouse.move(5, 300);
        await page.waitForTimeout(200);
        await page.screenshot({ path: "mock/screenshots/siteplan-" + c.name + "-large.png" });
        await page.getByTestId("siteplan-close").click();
        await expect(page.getByTestId("siteplan-flyout")).toHaveCount(0);
        expect(errors).toEqual([]);
    });
}
