// Playtest feedback "more vertically space conscious — grids, not a file tree; a little wider, multiple
// columns when it looks decent": grid browser (category chips ▸ type tiles ▸ preset chips), a resizable
// panel (left edge, remembered, 360rem … 45 % of the screen) and 1 ↔ 2 column content.
import { expect, Page, test } from "@playwright/test";
import { drawPolygon, FIELD, noTextOverflow, open, panelShot, waitPlanned, pickType } from "./helpers";

async function toDesign(page: Page, recipe = "park") {
    await drawPolygon(page, FIELD);
    await page.getByTestId("primary").click();
    await pickType(page, recipe);
    await waitPlanned(page);
}

const width = async (page: Page) => Number(await page.getByTestId("placemaker-wrap").getAttribute("data-width"));
const box = async (page: Page, id: string) => (await page.getByTestId(id).first().boundingBox())!;

/** Drags the panel's left edge by dx pixels (negative = wider). */
async function dragEdge(page: Page, dx: number) {
    const b = await box(page, "panel-resize");
    const x = b.x + b.width / 2,
        y = b.y + 40;
    await page.mouse.move(x, y);
    await page.mouse.down();
    for (let i = 1; i <= 6; i++) await page.mouse.move(x + (dx * i) / 6, y);
    await page.mouse.up();
}

test("picker: 1 Category · 2 Type · 3 Style rows of chips, compact", async ({ page }) => {
    const errors = await open(page);
    await page.evaluate(() => {
        localStorage.removeItem("placemaker.browserCategory.v2");
        localStorage.removeItem("placemaker.categoryUse.v1");
    });
    await toDesign(page, "park");
    expect(await width(page)).toBe(560); // 1920 px wide screen → the wide default

    // categories in a fixed order (the shelf: roads, parking, parks, shops & squares, nature); squares fold into shops
    const chips = await page.locator('[data-testid="make-groups"] [data-testid^="make-group-"]').evaluateAll((els) => els.map((e) => e.getAttribute("data-testid")));
    expect(chips).toEqual(["make-group-roads", "make-group-parking", "make-group-parks", "make-group-shops", "make-group-nature"]);
    await expect(page.getByTestId("make-group-parks")).toHaveAttribute("aria-pressed", "true");
    await expect(page.getByTestId("recipe-park")).toHaveAttribute("aria-pressed", "true");

    // styles of the selected type as chips, several per row, ending with "+ New"
    const presets = page.getByTestId("presets-park");
    for (const id of ["standard", "formal", "wild", "pond", "new"]) await expect(presets.getByTestId(`preset-${id}`)).toBeVisible();
    const d = await box(page, "preset-standard");
    const f = await box(page, "preset-formal");
    expect(Math.abs(f.y - d.y)).toBeLessThan(2);
    expect(f.x).toBeGreaterThan(d.x);
    // chips are one 24rem line each
    expect(Math.round(d.height)).toBe(24);

    // the whole picker in a few lines
    const browser = await box(page, "type-browser");
    expect(browser.height).toBeLessThan(130);
    await noTextOverflow(page, "picker wide");
    await panelShot(page, "grid-browser-wide");

    // a category lists its types; switching category picks its first type, so the styles follow
    await page.getByTestId("make-group-shops").click();
    await expect(page.getByTestId("make-group-shops")).toHaveAttribute("aria-pressed", "true");
    await expect(page.getByTestId("recipe-plaza")).toBeVisible();
    await expect(page.getByTestId("recipe-park")).toHaveCount(0);
    await expect(page.getByTestId("make-group-shops")).toHaveAttribute("data-has-selected", "true");
    await pickType(page, "plaza");
    await expect(page.getByTestId("presets-plaza").getByTestId("preset-civic")).toBeVisible();
    await page.getByTestId("make-group-parks").click();
    await expect(page.getByTestId("recipe-park")).toBeVisible();

    // search: style hits of any type, marked with their type
    await page.getByTestId("browser-search-toggle").click();
    await page.getByTestId("browser-search").fill("meadow");
    await expect(page.getByTestId("preset-hits").getByTestId("preset-wild")).toBeVisible();
    await expect(page.getByTestId("recipe-park")).toBeVisible();
    await expect(page.getByTestId("recipe-plaza")).toHaveCount(0);
    await panelShot(page, "grid-browser-search");
    await page.getByTestId("browser-search").press("Enter");
    await expect.poll(() => page.evaluate(() => (window as any).pm.backend.active?.id)).toBe("wild");
    expect(errors).toEqual([]);
});

test("narrow screen: 400rem default, chips wrap, section heads still in two columns", async ({ page }) => {
    const errors = await open(page, { width: 1440, height: 900 });
    await toDesign(page, "park");
    expect(await width(page)).toBe(400);
    await expect(page.getByTestId("placemaker-wrap")).toHaveAttribute("data-cols", "1");
    await expect(page.getByTestId("step-0").getByTestId("control-list").first()).toHaveAttribute("data-cols", "2");
    await expect(page.getByTestId("feature-grid")).toHaveAttribute("data-cols", "2");
    await expect(page.getByTestId("control-grid")).toHaveCount(0);
    await noTextOverflow(page, "narrow");
    await panelShot(page, "grid-park-narrow-default");
    expect(errors).toEqual([]);
});

test("resize by the left edge: wider / narrower, clamped, remembered; double-click resets", async ({ page }) => {
    await open(page);
    await toDesign(page, "parking");
    const panel0 = await box(page, "placemaker-panel");
    // narrower
    await dragEdge(page, 180);
    expect(await width(page)).toBeLessThan(400);
    const panel1 = await box(page, "placemaker-panel");
    expect(Math.abs(panel1.x + panel1.width - (panel0.x + panel0.width))).toBeLessThan(2); // right edge stays docked
    await expect(page.getByTestId("placemaker-wrap")).toHaveAttribute("data-cols", "1");
    // never below the minimum
    await dragEdge(page, 400);
    expect(await width(page)).toBe(360);
    await noTextOverflow(page, "360rem");
    // never above 45 % of the screen
    await dragEdge(page, -800);
    expect(await width(page)).toBe(Math.floor(1920 * 0.45));
    await expect(page.getByTestId("placemaker-wrap")).toHaveAttribute("data-cols", "2");
    await noTextOverflow(page, "max width");
    // remembered
    await page.reload();
    await expect(page.getByTestId("placemaker-panel")).toBeVisible();
    expect(await width(page)).toBe(Math.floor(1920 * 0.45));
    // double-click: back to the default for this screen
    await page.getByTestId("panel-resize").dblclick();
    expect(await width(page)).toBe(560);
});

test("resize a floating panel: its right edge stays where it is", async ({ page }) => {
    await open(page);
    const h = await box(page, "panel-header");
    await page.mouse.move(h.x + 120, h.y + h.height / 2);
    await page.mouse.down();
    await page.mouse.move(h.x - 300, h.y + 100, { steps: 6 });
    await page.mouse.up();
    await expect(page.getByTestId("placemaker-wrap")).toHaveAttribute("data-docked", "false");
    const before = await box(page, "placemaker-panel");
    await dragEdge(page, -120);
    const after = await box(page, "placemaker-panel");
    expect(after.width).toBeGreaterThan(before.width + 100);
    expect(Math.abs(after.x + after.width - (before.x + before.width))).toBeLessThan(3);
});

test("two columns: section heads and extras side by side, an open one unfolds under its pair", async ({ page }) => {
    const errors = await open(page);
    await page.evaluate(() => localStorage.removeItem("placemaker.sections.v1"));
    await toDesign(page, "park");
    const step = page.getByTestId("step-0");
    // every section starts folded, heads two per line
    await expect(step.getByTestId("group-basics")).toHaveAttribute("data-open", "false");
    const g0 = await box(page, "group-basics");
    const g1 = (await step.locator('[data-testid^="group-"][data-open]').nth(1).boundingBox())!;
    expect(Math.abs(g0.y - g1.y)).toBeLessThan(2);
    expect(g1.x).toBeGreaterThan(g0.x + g0.width - 4);
    // opening one: its controls unfold full width under the pair, the heads stay where they are
    await step.getByTestId("group-basics-toggle").click();
    await expect(step.getByTestId("group-basics")).toHaveAttribute("data-open", "true");
    const body = (await step.getByTestId("group-basics-body").boundingBox())!;
    expect(body.y).toBeGreaterThan(g0.y + g0.height - 1);
    expect(body.width).toBeGreaterThan(g0.width * 1.8);
    const g0b = await box(page, "group-basics");
    expect(Math.abs(g0b.y - g0.y)).toBeLessThan(2);
    // short slider labels pair up inside
    const rows = step.locator('[data-testid="group-basics-body"] [data-testid^="control-"][data-kind="slider"]');
    expect(await rows.count()).toBeGreaterThanOrEqual(2);
    const a = (await rows.nth(0).boundingBox())!;
    const b = (await rows.nth(1).boundingBox())!;
    expect(Math.abs(a.y - b.y)).toBeLessThan(2);
    await step.getByTestId("group-basics-toggle").click();

    // extras in two columns; an open one unfolds under its pair
    await expect(page.getByTestId("feature-grid")).toHaveAttribute("data-cols", "2");
    const f0 = await box(page, "feature-plaza");
    const f1 = await box(page, "feature-playground");
    expect(Math.abs(f0.y - f1.y)).toBeLessThan(2);
    expect(f0.width).toBeLessThan(300);
    await page.getByTestId("feature-toggle-playground").click();
    await waitPlanned(page);
    const fold = page.getByTestId("feature-fold-playground");
    if (await fold.count()) {
        await fold.click();
        await expect(page.getByTestId("feature-playground")).toHaveAttribute("data-open", "true");
        expect((await box(page, "feature-options-playground")).width).toBeGreaterThan(450);
        await noTextOverflow(page, "park wide, feature open");
        await panelShot(page, "grid-park-wide-open");
        await fold.click();
    }
    await noTextOverflow(page, "park wide");
    await panelShot(page, "grid-park-wide");

    // narrower than 380rem → one column
    await dragEdge(page, 200);
    await expect(page.getByTestId("feature-grid")).toHaveAttribute("data-cols", "1");
    await noTextOverflow(page, "park narrow");
    await panelShot(page, "grid-park-narrow");
    await page.getByTestId("make-group-parking").click();
    await pickType(page, "parking");
    await waitPlanned(page);
    await noTextOverflow(page, "parking narrow");
    await panelShot(page, "grid-parking-narrow");
    await dragEdge(page, -200);
    await noTextOverflow(page, "parking wide");
    await panelShot(page, "grid-parking-wide");
    expect(errors).toEqual([]);
});
