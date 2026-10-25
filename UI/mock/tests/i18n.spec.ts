// Every shipped language in the panel (mock ?lang=): the Area stage, the Design stage of the main types with every
// Parameters group open, Review; no text cut or spilling anywhere (noTextOverflow), a screenshot per language and stage
// in mock/screenshots/i18n-<locale>-*.png. PM_LANGS=de-DE,ja-JP narrows the run.
import { expect, Page, test } from "@playwright/test";
import * as fs from "fs";
import * as path from "path";
import { drawPolygon, FIELD, noTextOverflow, open, panelShot, pickType, unfoldSections, waitPlanned } from "./helpers";

const LANGS = (process.env.PM_LANGS ?? "de-DE,es-ES,fr-FR,it-IT,ja-JP,ko-KR,pl-PL,pt-BR,ru-RU,zh-HANS,zh-HANT").split(",");
const TYPES: [string, string][] = [
    ["parks", "park"],
    ["parking", "parking"],
    ["roads", "roads"],
    ["shops", "stripMall"],
    ["shops", "plaza"],
];

async function pick(page: Page, group: string, recipe: string) {
    const chip = page.getByTestId(`make-group-${group}`);
    if (!(await chip.count())) await page.getByTestId("make-groups-more").click();
    await page.getByTestId(`make-group-${group}`).click();
    await pickType(page, recipe);
    await waitPlanned(page);
    await page.waitForTimeout(150);
}

// The English run goes first and writes what is already cut in English (long asset names and the like): a language is
// only blamed for what it adds.
const BASELINE = path.join(__dirname, "..", "screenshots", "i18n-en-US-cut.json");

/** noTextOverflow's findings (the texts), collected instead of failing at the first. */
async function overflow(page: Page, where: string, out: { where: string; text: string; line: string }[]) {
    try {
        await noTextOverflow(page, where);
    } catch (e) {
        // eslint-disable-next-line no-control-regex
        for (const line of String((e as Error).message).replace(/\u001b\[\d+m/g, "").split("\n")) {
            const m = /(cut|spills): \\?"(.*?)\\?" (.*?)",?$/.exec(line.trim());
            if (m) out.push({ where, text: m[2], line: `${m[1]} "${m[2]}" ${m[3]}` });
        }
    }
}

for (const lang of ["en-US", ...LANGS]) {
    test(`${lang}: no text cut or spilling in the panel`, async ({ page }) => {
        test.setTimeout(120_000);
        const errors = await open(page, { real: true, lang });
        await page.waitForFunction(() => (window as any).pm.backend.hostUp !== undefined);
        await page.evaluate(() => {
            localStorage.removeItem("placemaker.sections.v1");
            localStorage.removeItem("placemaker.browserCategory.v2");
        });
        const bad: { where: string; text: string; line: string }[] = [];
        await overflow(page, "area", bad);
        await panelShot(page, `i18n-${lang}-1-area`);
        await drawPolygon(page, FIELD);
        await page.getByTestId("primary").click();
        for (const [group, recipe] of TYPES) {
            await pick(page, group, recipe);
            await overflow(page, recipe, bad);
            await panelShot(page, `i18n-${lang}-2-${recipe}`);
            await unfoldSections(page);
            await overflow(page, recipe + " open", bad);
        }
        // Review: roads need no entrance
        await pick(page, "roads", "roads");
        await page.getByTestId("primary").click();
        await waitPlanned(page);
        await overflow(page, "review", bad);
        await panelShot(page, `i18n-${lang}-3-review`);
        if (lang === "en-US") {
            fs.writeFileSync(BASELINE, JSON.stringify(bad.map((b) => b.text), null, 1));
            return;
        }
        const english = new Set<string>(fs.existsSync(BASELINE) ? JSON.parse(fs.readFileSync(BASELINE, "utf8")) : []);
        const own = bad.filter((b) => !english.has(b.text)).map((b) => `${b.where}: ${b.line}`);
        fs.writeFileSync(path.join(__dirname, "..", "screenshots", `i18n-${lang}-cut.json`), JSON.stringify(own, null, 1));
        expect(own, lang).toEqual([]);
        expect(errors).toEqual([]);
    });
}
