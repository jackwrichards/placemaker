// Round 4 (playtest: "I need to see the texture"): thumbnails in asset rows, a big picker grid with
// surface kinds / decal themes as tabs, hover = live map preview (previewValue / endPreview), click =
// commit (setValue); the Sketch | Real preview toggle; a clear "Planning…" indicator; a compact Review
// that never overlaps; the Make row grouped once there are many types.
import { expect, Page, test } from "@playwright/test";
import { drawPolygon, FIELD, noTextOverflow, open, panelShot, shot, SHOTS, waitPlanned, pickType, unfoldSections } from "./helpers";

async function toDesign(page: Page, recipe: string) {
    await drawPolygon(page, FIELD);
    await page.getByTestId("primary").click();
    await pickType(page, recipe);
    await waitPlanned(page);
}

async function unfold(page: Page, group: string) {
    const g = page.getByTestId("step-0").getByTestId("group-" + group).first();
    if ((await g.getAttribute("data-open")) !== "true") await g.getByTestId("group-" + group + "-toggle").click();
    await expect(g).toHaveAttribute("data-open", "true");
}

type Call = { name: string; args: unknown[] };
const calls = (page: Page, from: number): Promise<Call[]> =>
    page.evaluate((n) => (window as any).pm.triggerLog.slice(n).map((c: any) => ({ name: c.name, args: c.args })), from);
/** Clears the (capped, shifting) trigger log; the calls after this are read from index 0. */
const logLength = (page: Page): Promise<number> =>
    page.evaluate(() => {
        (window as any).pm.triggerLog.length = 0;
        return 0;
    });

/** The <img> (or initials swatch) is really there and has a size. */
async function visibleThumb(page: Page, testId: string) {
    const el = page.getByTestId(testId);
    await expect(el).toBeVisible();
    const box = (await el.boundingBox())!;
    expect(box.width).toBeGreaterThanOrEqual(20);
    expect(box.height).toBeGreaterThanOrEqual(20);
    if ((await el.getAttribute("data-thumb")) === "img")
        expect(await el.evaluate((i) => (i as HTMLImageElement).complete && (i as HTMLImageElement).naturalWidth > 0)).toBe(true);
}

test("asset rows show the chosen thumbnail; Automatic shows a mosaic of the pool", async ({ page }) => {
    await open(page);
    await toDesign(page, "parking");
    await unfoldSections(page);
    const row = page.getByTestId("control-surface");
    await expect(page.getByTestId("asset-thumb-surface")).toHaveAttribute("data-thumb", "mosaic");
    await expect(page.getByTestId("asset-thumb-surface").locator('[data-thumb="img"]')).toHaveCount(3);
    expect((await row.boundingBox())!.height).toBeGreaterThanOrEqual(30);

    await page.getByTestId("asset-button-surface").click();
    await page.getByTestId("asset-concrete").click();
    await expect(page.getByTestId("asset-thumb-surface")).toHaveAttribute("data-thumb", "img");
    await visibleThumb(page, "asset-thumb-surface");
    const box = (await page.getByTestId("asset-thumb-surface").boundingBox())!;
    expect(box.width).toBeGreaterThanOrEqual(26); // ~28rem swatch at 1080p
    await panelShot(page, "r4-asset-row-thumbnail");
});

test("surface picker: big swatch grid, Grass / Paving / Natural / Asphalt tabs, search across tabs, initials fallback", async ({ page }) => {
    await open(page);
    await toDesign(page, "plaza");
    await unfold(page, "basics");
    const btn = page.getByTestId("asset-button-surface");
    await btn.scrollIntoViewIfNeeded();
    await btn.click();
    const picker = page.getByTestId("asset-picker");
    await expect(picker).toBeVisible();
    // opens on the control's own kind (paving), all four kinds offered
    for (const k of ["all", "surface.grass", "surface.paving", "surface.natural", "surface.asphalt"]) await expect(page.getByTestId("asset-tab-" + k)).toBeVisible();
    await expect(page.getByTestId("asset-tab-surface.paving")).toHaveAttribute("aria-pressed", "true");
    await expect(page.getByTestId("asset-herringbone-brick")).toBeVisible();
    await expect(page.getByTestId("asset-lawn")).toHaveCount(0);
    // big tiles
    const tile = (await page.getByTestId("asset-herringbone-brick").boundingBox())!;
    expect(tile.width).toBeGreaterThanOrEqual(80);
    // a modded surface without a thumbnail → initials, never an empty box
    const mod = page.getByTestId("asset-mod-mossy-cobbles").locator('[data-thumb="initials"]');
    await expect(mod).toHaveText("MC");
    await panelShot(page, "r4-picker-surfaces");

    await page.getByTestId("asset-tab-surface.grass").click();
    await expect(page.getByTestId("asset-lawn")).toBeVisible();
    await expect(page.getByTestId("asset-herringbone-brick")).toHaveCount(0);
    await page.getByTestId("asset-search").fill("sand");
    await expect(page.getByTestId("asset-sand")).toBeVisible();
    await expect(page.getByTestId("asset-tabs")).toHaveCount(0);
    await noTextOverflow(page, "surface picker");
});

test("hover previews on the map (previewValue), leaving / closing ends it (endPreview), click commits (setValue)", async ({ page }) => {
    await open(page);
    await toDesign(page, "parking");
    await unfoldSections(page);
    await page.getByTestId("asset-button-surface").click();
    await expect(page.getByTestId("asset-concrete")).toBeVisible();

    let from = await logLength(page);
    await page.getByTestId("asset-concrete").hover();
    await expect.poll(async () => (await calls(page, from)).filter((c) => c.name === "previewValue").map((c) => c.args)).toEqual([["0", "surface", '"concrete"']]);
    expect(await page.evaluate(() => (window as any).pm.backend.preview.get("0|surface"))).toBe("concrete");
    await page.getByTestId("asset-worn-asphalt").hover();
    await expect.poll(async () => (await calls(page, from)).filter((c) => c.name === "previewValue").length).toBe(2);
    expect((await calls(page, from)).some((c) => c.name === "endPreview")).toBe(false); // no flicker between tiles
    // (no shot() here: it parks the mouse, which would end the preview)
    await page.screenshot({ path: SHOTS + "/r4-hover-preview.png" });

    // leave the grid → back to the committed value, preview ended
    from = await logLength(page);
    await page.mouse.move(5, 300);
    await expect.poll(async () => (await calls(page, from)).map((c) => c.name)).toEqual(["endPreview"]);
    expect(await page.evaluate(() => (window as any).pm.backend.preview.size)).toBe(0);
    expect(await page.evaluate(() => (window as any).pm.backend.root.values.surface ?? "")).toBe("");

    // close without choosing (Esc) while hovering → ended
    await page.getByTestId("asset-concrete").hover();
    from = await logLength(page);
    await page.keyboard.press("Escape");
    await expect(page.getByTestId("asset-picker")).toHaveCount(0);
    await expect.poll(async () => (await calls(page, from)).map((c) => c.name)).toContain("endPreview");

    // click commits with setValue
    await page.getByTestId("asset-button-surface").click();
    await page.getByTestId("asset-concrete").hover();
    from = await logLength(page);
    await page.getByTestId("asset-concrete").click();
    await expect(page.getByTestId("asset-picker")).toHaveCount(0);
    const after = await calls(page, from);
    expect(after.map((c) => c.name)).toContain("setValue");
    expect(after.find((c) => c.name === "setValue")!.args).toEqual(["0", "surface", '"concrete"']);
    expect(after.map((c) => c.name)).not.toContain("endPreview");
    expect(await page.evaluate(() => (window as any).pm.backend.root.values.surface)).toBe("concrete");
});

test("decals: multi-select picker with theme tabs, chips with thumbnails, density controls from the schema", async ({ page }) => {
    await open(page);
    await toDesign(page, "park");
    await unfold(page, "ground");
    await expect(page.getByTestId("slider-decalDensity")).toBeVisible();
    await expect(page.getByTestId("slider-decalClustering")).toBeVisible();
    await expect(page.getByTestId("asset-thumb-decals")).toBeVisible();
    await page.getByTestId("assetmulti-add-decals").click();
    for (const k of ["all", "theme:leaves", "theme:cracks", "theme:puddles", "theme:markings"]) await expect(page.getByTestId("asset-tab-" + k)).toBeVisible();
    await page.getByTestId("asset-tab-theme:leaves").click();
    await page.getByTestId("asset-decal-autumn-leaves").click();
    await page.getByTestId("asset-tab-theme:puddles").click();
    await page.getByTestId("asset-decal-puddle").click();
    await panelShot(page, "r4-picker-decals");
    await page.getByTestId("asset-picker-done").click();
    await visibleThumb(page, "chip-thumb-decal-autumn-leaves");
    await visibleThumb(page, "chip-thumb-decal-puddle");
    expect(await page.evaluate(() => (window as any).pm.backend.root.values.decals)).toEqual(["decal-autumn-leaves", "decal-puddle"]);
    await noTextOverflow(page, "decals");
    await panelShot(page, "r4-decals-chips");
});

test("preview: Design is always Sketch (no switch); Review opens on Real with the Sketch | Real switch, spinner while the ghost rebuilds", async ({ page }) => {
    await open(page);
    await toDesign(page, "plaza");
    await expect(page.getByTestId("preview-mode")).toHaveCount(0);
    await page.evaluate(() => ((window as any).pm.backend.ghostMs = 1500));
    await page.getByTestId("primary").click();
    await expect(page.getByTestId("stage-review")).toBeVisible();
    const toggle = page.getByTestId("preview-mode");
    await expect(toggle).toHaveAttribute("data-mode", "ghost");
    await expect(page.getByTestId("preview-mode-ghost")).toHaveAttribute("aria-pressed", "true");
    let from = await logLength(page);
    await page.getByTestId("preview-mode-sketch").click();
    expect((await calls(page, from)).find((c) => c.name === "setPreviewMode")!.args).toEqual(["sketch"]);
    await expect(toggle).toHaveAttribute("data-mode", "sketch");
    from = await logLength(page);
    await page.getByTestId("preview-mode-ghost").click();
    await expect(toggle).toHaveAttribute("data-mode", "ghost");
    await expect(page.getByTestId("ghost-pending")).toBeVisible();
    await panelShot(page, "r4-preview-real-pending");
    await expect(page.getByTestId("ghost-pending")).toHaveCount(0, { timeout: 4000 });
    await shot(page, "r4-real-preview-map");
    await noTextOverflow(page, "header with preview toggle");
});

test("planning shows a spinner + 'Planning…' and dims the stale numbers", async ({ page }) => {
    await open(page);
    await toDesign(page, "park");
    await page.evaluate(() => {
        const b = (window as any).pm.backend;
        b.stale = true;
        b.planning = true;
        b.planProgress = 0.42;
        b.publishAll();
    });
    await expect(page.getByTestId("busy")).toContainText("Planning");
    await expect(page.getByTestId("busy")).toContainText("42%");
    await expect(page.getByTestId("busy-bar")).toBeVisible();
    await expect(page.getByTestId("quick-numbers")).toHaveAttribute("data-stale", "true");
    await noTextOverflow(page, "footer while planning");
    await panelShot(page, "r4-planning");
});

test("Review is compact: issues as short chips (tooltip has the sentence), +N more, breakdown behind the cost bar; nothing overlaps", async ({ page }) => {
    await open(page);
    await toDesign(page, "park");
    await page.getByTestId("primary").click();
    await expect(page.getByTestId("stage-review")).toBeVisible();
    await waitPlanned(page);
    const long = "This is a deliberately very long issue text that would wrap over several lines and used to overlap the next line in Gameface";
    // Seven long errors and warnings (Warnings), one note (Checks) (NoRoom = "{0}: no free spot left, skipped.") and a two-layer breakdown.
    await page.evaluate((text) => {
        const raw = (window as any).pm.backend.summary();
        raw.issues = Array.from({ length: 7 }, (_, i) => ({ severity: ["error", "warning"][i % 2], text: { key: "Placemaker.Test.Issue" + i, args: [text + " #" + i] }, stepPath: "0", at: [600, 0, 380] }));
        raw.issues.push({ severity: "info", text: { key: "Placemaker.Test.Note", args: ["a note"] }, stepPath: "0" });
        // s45: the same issue from two layers is one chip
        raw.issues.push({ severity: "warning", text: { key: "Placemaker.Test.Issue1", args: [text + " again"] }, stepPath: "0" });
        raw.layers = [{ stepPath: "0", layer: "park", regionKey: "root", stats: [], cost: 1000, upkeep: 10 }, { stepPath: "0/0", layer: "plaza", regionKey: "core", stats: [], cost: 2000, upkeep: 20 }];
        (window as any).pmPublish("Placemaker", "summary", JSON.stringify(raw));
    }, long);
    const issues = page.getByTestId("issues");
    await expect(page.getByTestId("issues-more")).toHaveText("+2 more");
    // each issue is one short chip
    const chips = issues.locator('[data-testid^="issue-"]');
    expect(await chips.count()).toBe(5);
    for (const b of await chips.all()) expect((await b.boundingBox())!.height).toBeLessThanOrEqual(24);
    // errors first
    await expect(chips.first()).toHaveAttribute("data-testid", "issue-error");
    // notes are in Checks, apart from the warnings
    await expect(page.getByTestId("checks").locator('[data-testid="issue-info"]')).toHaveCount(1);
    await expect(issues.locator('[data-testid="issue-info"]')).toHaveCount(0);
    await expect(page.getByTestId("breakdown")).toHaveAttribute("data-open", "false");
    await expect(page.getByTestId("cost-bar")).toBeVisible();
    await noTextOverflow(page, "review with long issues");
    await panelShot(page, "r4-review-compact");
    await page.getByTestId("issues-more").click();
    await expect(page.getByTestId("issues-more")).toHaveText("Fewer");
    await page.getByTestId("breakdown").click();
    await expect(page.getByTestId("breakdown")).toHaveAttribute("data-open", "true");
    await expect(page.getByTestId("breakdown-list")).toBeVisible();
    await noTextOverflow(page, "review expanded");
    // no sibling rows overlap vertically
    const overlaps = await page.getByTestId("stage-review").evaluate((root) => {
        const out: string[] = [];
        const walk = (el: Element) => {
            const kids = Array.from(el.children).filter((k) => (k as HTMLElement).getBoundingClientRect().height > 0);
            for (let i = 1; i < kids.length; i++) {
                const a = kids[i - 1].getBoundingClientRect(), b = kids[i].getBoundingClientRect();
                const sameRow = Math.abs(a.top - b.top) < 1;
                if (!sameRow && b.top < a.bottom - 1 && b.left < a.right && a.left < b.right) out.push((kids[i].textContent ?? "").slice(0, 40));
            }
            kids.forEach(walk);
        };
        walk(root);
        return out;
    });
    expect(overlaps).toEqual([]);
    await panelShot(page, "r4-review-expanded");
});

test("Make: many types — the browser opens on the selected type's category; chips switch it; the choice is remembered", async ({ page }) => {
    await open(page);
    await drawPolygon(page, FIELD);
    await page.getByTestId("primary").click();
    await page.evaluate(() => (window as any).pm.backend.addMoreTypes());
    const recipes = page.getByTestId("recipes");
    await expect(recipes).toHaveAttribute("data-grouped", "true");
    await expect(page.getByTestId("make-group-parks")).toHaveAttribute("aria-pressed", "true");
    // gardens fold into parks
    for (const id of ["dogPark", "park", "botanicalGarden", "formalGarden"]) await expect(recipes.getByTestId(`recipe-${id}`)).toBeVisible();
    await expect(page.getByTestId("make-group-gardens")).toHaveCount(0);
    await expect(recipes.getByTestId("recipe-marketSquare")).toHaveCount(0);
    // industry and transport are shelved: no chip, no type, no search hit
    for (const g of ["industry", "transport", "industrial", "squares", "events", "water", "gardens"]) await expect(page.getByTestId(`make-group-${g}`)).toHaveCount(0);
    await page.getByTestId("browser-search-toggle").click();
    await page.getByTestId("browser-search").fill("quarry");
    await expect(page.getByTestId("browser-empty")).toBeVisible();
    await page.getByTestId("browser-search").fill("rest area");
    await expect(page.getByTestId("browser-empty")).toBeVisible();
    await page.getByTestId("browser-clear").click();
    // tiles share rows
    const ys = await recipes.locator('[data-testid^="recipe-"]').evaluateAll((els) => els.map((e) => Math.round(e.getBoundingClientRect().top)));
    expect(new Set(ys).size).toBeLessThan(ys.length);
    // squares fold into shops
    await page.getByTestId("make-group-shops").click();
    await expect(recipes.getByTestId("recipe-marketSquare")).toBeVisible();
    await expect(recipes.getByTestId("recipe-plaza")).toBeVisible();
    await expect(recipes.getByTestId("recipe-park")).toHaveCount(0);
    // switching category picks its first type, so the styles follow
    await expect(page.getByTestId("make-group-shops")).toHaveAttribute("data-has-selected", "true");
    await expect(page.getByTestId("make-group-parks")).toHaveAttribute("data-has-selected", "false");
    await page.getByTestId("make-group-parking").click();
    await expect(recipes.getByTestId("recipe-parkingGarage")).toBeVisible();
    await noTextOverflow(page, "grouped make row");
    await panelShot(page, "r4-make-grouped");
    await page.getByTestId("make-group-shops").click();
    await page.reload();
    await expect(page.getByTestId("placemaker-panel")).toBeVisible();
    await drawPolygon(page, FIELD);
    await page.getByTestId("primary").click();
    await page.evaluate(() => (window as any).pm.backend.addMoreTypes());
    await expect(page.getByTestId("make-group-shops")).toHaveAttribute("aria-pressed", "true");
});

// (core) The game switches a shelved LAST pick to the first type shown when it restores it (PlacemakerToolSystem.EnsureInitialized,
// checked in the game runner). A shelved type picked on purpose (the game runner by id, editing an old build) stays: the panel
// shows no chip for it but still shows its styles.
test("Make: a shelved type picked by id stays, with no tile, and its styles still show", async ({ page }) => {
    await open(page);
    await drawPolygon(page, FIELD);
    await page.getByTestId("primary").click();
    await page.evaluate(() => (window as any).pm.backend.addMoreTypes());
    await page.evaluate(() => (window as any).pm.backend.onTrigger("selectRecipe", ["quarry"]));
    await page.waitForTimeout(300);
    expect(await page.evaluate(() => (window as any).pm.backend.recipeId)).toBe("quarry");
    await expect(page.getByTestId("recipe-quarry")).toHaveCount(0);
    await expect(page.getByTestId("make-group-industry")).toHaveCount(0);
    await expect(page.getByTestId("presets-quarry")).toBeVisible();
});
