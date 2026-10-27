// The AI builder's chat card (src/components/AiChat.tsx) against the mock's scripted run (mock/backend/ai.ts): the welcome
// with examples and the mods switch, the progress (phases, spinner, time, Stop) while it works, the AI's actions grouped
// by its step, phase dividers, the finished chat, the mods switch locked, Start over and leaving asked first.
import { expect, Page, test } from "@playwright/test";
import { drawPolygon, FIELD, noTextOverflow, open, panelShot, pickType } from "./helpers";

async function toAi(page: Page) {
    await drawPolygon(page, FIELD);
    await page.getByTestId("primary").click();
    await pickType(page, "ai");
    await expect(page.getByTestId("ai-chat")).toBeVisible();
}

test("AI chat: welcome, work in progress, finished, mods lock, leave and start over asked first", async ({ page }) => {
    const errors = await open(page, { real: true, width: 1600, height: 1000 });
    await page.goto("/?aiStep=350");
    await expect(page.getByTestId("placemaker-panel")).toBeVisible();
    await toAi(page);

    // welcome: examples fill the box, the mods switch works before the first message
    await expect(page.getByTestId("ai-welcome")).toBeVisible();
    await expect(page.getByTestId("ai-example")).toHaveCount(4);
    await page.getByTestId("ai-mods").click();
    await expect(page.getByTestId("ai-mods")).toHaveAttribute("aria-checked", "true");
    await page.getByTestId("ai-mods").click();
    await expect(page.getByTestId("ai-mods")).toHaveAttribute("aria-checked", "false");
    await page.getByTestId("ai-example").first().click();
    await expect(page.getByTestId("ai-input")).toHaveValue(/park/i);
    await panelShot(page, "ai-chat-1-welcome");

    // working: the progress row with its spinner, Stop, the input locked
    await page.getByTestId("ai-send").click();
    await expect(page.getByTestId("ai-working")).toBeVisible();
    await expect(page.getByTestId("ai-spinner")).toBeVisible();
    await expect(page.getByTestId("ai-input")).toBeDisabled();
    await expect(page.getByTestId("ai-round").first()).toBeVisible();
    await page.waitForTimeout(800);
    await panelShot(page, "ai-chat-2-working");

    // finished: the AI's note, older steps folded, the latest open, phases as dividers, the mods choice locked
    await expect(page.getByTestId("ai-line-ai")).toBeVisible({ timeout: 10000 });
    await expect(page.getByTestId("ai-working")).toHaveCount(0);
    await expect(page.getByTestId("ai-phase-line")).toHaveCount(2);
    const rounds = page.getByTestId("ai-round");
    await expect(rounds).toHaveCount(5);
    await expect(rounds.nth(4).getByTestId("ai-tool")).toHaveCount(2);
    await expect(rounds.nth(0).getByTestId("ai-tool")).toHaveCount(0);
    await rounds.nth(2).locator("div").first().click();
    await expect(rounds.nth(2).getByTestId("ai-tool")).toHaveCount(4);
    await expect(rounds.nth(2).getByTestId("ai-tool-problems")).toHaveCount(1);
    await expect(page.getByTestId("ai-mods-locked")).toBeVisible();
    await expect(page.getByTestId("ai-mods")).toHaveCount(0);
    await noTextOverflow(page, "ai chat finished");
    await panelShot(page, "ai-chat-3-done");

    // leaving asks first; Cancel keeps everything
    await page.getByTestId("close").click().catch(() => undefined);
    const dialog = page.getByTestId("confirm-dialog");
    if (await dialog.count()) {
        await panelShot(page, "ai-chat-4-leave");
        await page.getByTestId("confirm-cancel").click();
        await expect(page.getByTestId("ai-line-ai")).toBeVisible();
    }
    await page.getByTestId("ai-reset").click();
    await expect(page.getByTestId("confirm-dialog")).toBeVisible();
    await page.getByTestId("confirm-ok").click();
    await expect(page.getByTestId("ai-welcome")).toBeVisible();
    expect(errors).toEqual([]);
});

for (const lang of ["de-DE", "ru-RU", "fr-FR", "ja-JP"])
    test(`AI chat in ${lang}: nothing cut or spilling (welcome, working, done)`, async ({ page }) => {
        const errors = await open(page, { real: true, width: 1600, height: 1000, lang });
        await page.goto(`/?lang=${lang}&aiStep=250`);
        await expect(page.getByTestId("placemaker-panel")).toBeVisible();
        await toAi(page);
        await noTextOverflow(page, lang + " ai welcome");
        await page.getByTestId("ai-example").first().click();
        await page.getByTestId("ai-send").click();
        await expect(page.getByTestId("ai-working")).toBeVisible();
        await page.waitForTimeout(600);
        await noTextOverflow(page, lang + " ai working");
        await expect(page.getByTestId("ai-line-ai")).toBeVisible({ timeout: 10000 });
        await noTextOverflow(page, lang + " ai done");
        await panelShot(page, `ai-chat-${lang}`);
        expect(errors).toEqual([]);
    });
