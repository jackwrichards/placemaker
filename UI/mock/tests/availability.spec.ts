// s15 — "When I can't see options, I should see them greyed out": controls, features and area modes that do not
// apply are SHOWN greyed out with a short reason; a UI newer than the DLL says "Restart the game".
import { expect, test } from "@playwright/test";
import { drawPolygon, FIELD, noTextOverflow, open, panelShot, waitPlanned, pickType } from "./helpers";

async function design(page: import("@playwright/test").Page, recipe: string) {
    await drawPolygon(page, FIELD);
    await page.getByTestId("primary").click();
    await pickType(page, recipe);
    await waitPlanned(page);
}

async function unfold(page: import("@playwright/test").Page, group: string) {
    const g = page.getByTestId("step-0").getByTestId("group-" + group).first();
    if ((await g.getAttribute("data-open")) !== "true") await g.getByTestId("group-" + group + "-toggle").click();
    await expect(g).toHaveAttribute("data-open", "true");
}

test("a control that does not apply is greyed out with the reason, and comes alive when it applies", async ({ page }) => {
    const errors = await open(page);
    await design(page, "park");
    await unfold(page, "water");
    const size = page.getByTestId("control-lakeSize");
    // Lake is off: the lake size is visible (no longer hidden) but disabled, saying why.
    await expect(size).toBeVisible();
    await expect(size).toHaveAttribute("data-disabled", "true");
    await expect(size).toHaveAttribute("data-reason", /Only when .+ is on/);
    await panelShot(page, "s15-disabled-control");
    // Its editor takes no input while disabled.
    const before = await page.evaluate(() => (window as any).pm.backend.root.values.lakeSize);
    await page.getByTestId("slider-lakeSize").click({ force: true });
    expect(await page.evaluate(() => (window as any).pm.backend.root.values.lakeSize)).toBe(before);
    // Turning the lake on makes it a normal control.
    await page.getByTestId("toggle-lake").click();
    await expect(size).not.toHaveAttribute("data-disabled", "true");
    expect(errors).toEqual([]);
});

test("a feature that cannot fit the area is listed greyed out with why; area modes the DLL lacks are greyed too", async ({ page }) => {
    const errors = await open(page);
    await design(page, "park");
    await page.evaluate(() => {
        const b = (window as any).pm.backend;
        b.layers.find((l: any) => l.id === "playground").minArea = 1e9;
        b.publishAll();
    });
    const row = page.getByTestId("feature-playground");
    await expect(row).toBeVisible();
    await expect(row).toHaveAttribute("data-disabled", "true");
    await expect(row).toHaveAttribute("data-reason", /bigger area/);
    await page.getByTestId("feature-toggle-playground").click({ force: true });
    await expect(row).toHaveAttribute("data-enabled", "false");
    await panelShot(page, "s15-disabled-feature");

    // A DLL that does not handle Block paint (older, or it reports the mode unavailable): greyed with a reason.
    await page.getByTestId("stagebar-area").click();
    await page.evaluate(() => {
        const b = (window as any).pm.backend;
        b.areaModes = b.areaModes.map((m: any) => (m.mode === "blockPaint" ? { ...m, enabled: false, reason: { key: "Placemaker.Why.Test", args: [] } } : m));
        b.publishAll();
    });
    // Paint area opens as plain paint; its Smart edges switch is greyed with the reason
    await page.getByTestId("area-mode-paint").click();
    expect(await page.evaluate(() => (window as any).pm.backend.mode)).toBe("paint");
    await expect(page.getByTestId("smart-edges-wrap")).toHaveAttribute("data-disabled", "true");
    await page.getByTestId("smart-edges").click({ force: true });
    expect(await page.evaluate(() => (window as any).pm.backend.mode)).not.toBe("blockPaint");
    expect(errors).toEqual([]);
});

test("UI newer than the DLL: restart notice, and modes it cannot know are greyed with restart", async ({ page }) => {
    const errors = await open(page);
    await expect(page.getByTestId("version-notice")).toHaveCount(0);
    await page.evaluate(() => {
        const b = (window as any).pm.backend;
        // An s11-era DLL: no protocol, no mode list, no brush rules.
        b.protocol = undefined;
        b.areaModes = undefined;
        b.brushRules = undefined;
        b.publishAll();
    });
    await expect(page.getByTestId("version-notice")).toBeVisible();
    await expect(page.getByTestId("version-notice")).toContainText("Restart the game");
    await expect(page.getByTestId("area-mode-paint")).not.toHaveAttribute("aria-disabled", "true");
    await page.getByTestId("area-mode-paint").click();
    expect(await page.evaluate(() => (window as any).pm.backend.mode)).toBe("paint");
    await expect(page.getByTestId("brush-size")).toBeVisible();
    await panelShot(page, "s15-version-notice");
    expect(errors).toEqual([]);
});

test("brush: Angle only for the square brush; Smart edges off greys the stops, one block, snap and smooth", async ({ page }) => {
    const errors = await open(page);
    await page.getByTestId("area-mode-paint").click();
    expect(await page.evaluate(() => (window as any).pm.backend.mode)).toBe("blockPaint");
    await expect(page.getByTestId("brush-angle-row")).toHaveCount(0);
    await page.getByTestId("brush-shape").getByText("Square").click();
    await expect(page.getByTestId("brush-angle-row")).toBeVisible();
    await expect(page.getByTestId("stop-builds")).toHaveAttribute("aria-pressed", "true");
    await expect(page.getByTestId("area-stops")).not.toHaveAttribute("data-disabled", "true");
    await page.getByTestId("smart-edges").click();
    expect(await page.evaluate(() => (window as any).pm.backend.mode)).toBe("paint");
    expect(await page.evaluate(() => (window as any).pm.backend.brushRules.anything)).toBe(true);
    await expect(page.getByTestId("area-stops")).toHaveAttribute("data-disabled", "true");
    await expect(page.getByTestId("area-zoning")).toHaveAttribute("data-disabled", "true");
    for (const k of ["roads", "paths", "buildings", "builds", "lots", "water"]) await expect(page.getByTestId(`stop-${k}`)).toHaveAttribute("aria-disabled", "true");
    await page.getByTestId("smart-edges").click();
    expect(await page.evaluate(() => (window as any).pm.backend.mode)).toBe("blockPaint");
    expect(errors).toEqual([]);
});

test("area overhaul: Smart edges off does not grey the stops for Points; reset brings the defaults back", async ({ page }) => {
    const errors = await open(page);
    await page.getByTestId("area-mode-paint").click();
    await page.getByTestId("smart-edges").click();
    await page.getByTestId("area-mode-draw").click();
    await expect(page.getByTestId("stop-builds")).not.toHaveAttribute("aria-disabled", "true");
    await expect(page.getByTestId("stop-builds")).toHaveAttribute("aria-pressed", "true");
    await page.getByTestId("stop-builds").click();
    await expect(page.getByTestId("stop-builds")).toHaveAttribute("aria-pressed", "false");
    await page.getByTestId("area-mode-paint").click();
    await page.getByTestId("brush-rules-reset").click();
    await page.getByTestId("area-mode-draw").click();
    await expect(page.getByTestId("stop-builds")).toHaveAttribute("aria-pressed", "true");
    await noTextOverflow(page, "stops row");
    expect(errors).toEqual([]);
});
