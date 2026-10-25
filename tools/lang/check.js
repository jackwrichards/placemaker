// The translations against the English: what is missing, what changed in English since it was translated, what is broken.
//
//   node tools/lang/check.js                    every shipped language, one line each (exit 1 when any is broken)
//   node tools/lang/check.js de-DE              that language, every problem listed
//   node tools/lang/check.js --todo de-DE park  what to translate in Lang/de-DE.park.json: {key: English}, missing or changed
//   node tools/lang/check.js --stamp de-DE      record the English each translated text was made from (after translating)
//
// Lang/<locale>.<area>.json mirrors Lang/en-US.<area>.json key for key. "Changed": tools/lang/state/<locale>.json keeps a hash
// of the English each text was translated from (--stamp); a key whose English hash differs is to be translated again.
// Broken: a {0} placeholder, a line break or a leading/trailing space that the English has and the translation not (or the
// other way round), an empty text, a key the English does not have, a file that is not valid JSON.
const fs = require("fs");
const path = require("path");
const crypto = require("crypto");

const REPO = path.resolve(__dirname, "..", "..");
const LANG = path.join(REPO, "Lang");
const STATE = path.join(__dirname, "state");
/** The game's languages (Cities2_Data/Content/Game/Locale.cok), English aside. */
const SHIPPED = ["de-DE", "es-ES", "fr-FR", "it-IT", "ja-JP", "ko-KR", "pl-PL", "pt-BR", "ru-RU", "zh-HANS", "zh-HANT"];

const hash = (s) => crypto.createHash("sha1").update(s, "utf8").digest("hex").slice(0, 12);
const read = (f) => JSON.parse(fs.readFileSync(f, "utf8").replace(/^﻿/, ""));

function areas() {
    return fs.readdirSync(LANG).filter((f) => /^en-US\.[^.]+\.json$/.test(f)).map((f) => f.slice(6, -5)).sort();
}

function loadState(locale) {
    const f = path.join(STATE, locale + ".json");
    return fs.existsSync(f) ? read(f) : {};
}

/** Placeholders and layout marks a text carries: {0}, line breaks, a leading or trailing space. */
function marks(s) {
    const ph = (s.match(/\{\d+\}/g) ?? []).sort().join(",");
    const nl = (s.match(/\n/g) ?? []).length;
    return `${ph}|${nl}|${/^\s/.test(s) ? "lead" : ""}|${/\s$/.test(s) ? "trail" : ""}`;
}

/** Every problem of one language: {area: {missing:[], changed:[], broken:[{key, why}], extra:[]}}. */
function check(locale) {
    const state = loadState(locale);
    const out = {};
    for (const area of areas()) {
        const en = read(path.join(LANG, `en-US.${area}.json`));
        const file = path.join(LANG, `${locale}.${area}.json`);
        const r = { missing: [], changed: [], broken: [], extra: [] };
        let tr = {};
        if (fs.existsSync(file)) {
            try {
                tr = read(file);
            } catch (e) {
                r.broken.push({ key: "(file)", why: "not valid JSON: " + e.message });
            }
        }
        for (const [k, v] of Object.entries(en)) {
            if (!(k in tr)) {
                r.missing.push(k);
                continue;
            }
            const t = tr[k];
            if (typeof t !== "string" || (!t.trim() && v.trim())) r.broken.push({ key: k, why: "empty" });
            else if (marks(t) !== marks(v)) r.broken.push({ key: k, why: `placeholders/line breaks differ: English ${JSON.stringify(marks(v))}, here ${JSON.stringify(marks(t))}` });
            if (state[k] && state[k] !== hash(v)) r.changed.push(k);
        }
        for (const k of Object.keys(tr)) if (!(k in en)) r.extra.push(k);
        out[area] = r;
    }
    return out;
}

function totals(res) {
    const t = { missing: 0, changed: 0, broken: 0, extra: 0 };
    for (const r of Object.values(res)) for (const k of Object.keys(t)) t[k] += r[k].length;
    return t;
}

const args = process.argv.slice(2);
if (args[0] === "--todo") {
    const [, locale, area] = args;
    const en = read(path.join(LANG, `en-US.${area}.json`));
    const r = check(locale)[area];
    const keys = new Set([...r.missing, ...r.changed, ...r.broken.map((b) => b.key)]);
    const todo = {};
    for (const k of Object.keys(en)) if (keys.has(k)) todo[k] = en[k];
    console.log(JSON.stringify(todo, null, 2));
} else if (args[0] === "--stamp") {
    const locale = args[1];
    const state = loadState(locale);
    let n = 0;
    for (const area of areas()) {
        const file = path.join(LANG, `${locale}.${area}.json`);
        if (!fs.existsSync(file)) continue;
        const en = read(path.join(LANG, `en-US.${area}.json`));
        const tr = read(file);
        for (const k of Object.keys(tr)) if (k in en) {
            state[k] = hash(en[k]);
            n++;
        }
    }
    fs.mkdirSync(STATE, { recursive: true });
    const sorted = {};
    for (const k of Object.keys(state).sort()) sorted[k] = state[k];
    fs.writeFileSync(path.join(STATE, locale + ".json"), JSON.stringify(sorted, null, 1) + "\n");
    console.log(`${locale}: stamped ${n} texts`);
} else if (args[0]) {
    const locale = args[0];
    const res = check(locale);
    let bad = false;
    for (const [area, r] of Object.entries(res)) {
        const parts = [];
        if (r.missing.length) parts.push(`${r.missing.length} missing`);
        if (r.changed.length) parts.push(`${r.changed.length} changed in English: ${r.changed.join(", ")}`);
        if (r.extra.length) parts.push(`${r.extra.length} not in English: ${r.extra.join(", ")}`);
        for (const b of r.broken) parts.push(`BROKEN ${b.key}: ${b.why}`);
        if (r.broken.length || r.extra.length) bad = true;
        if (parts.length) console.log(`${locale}.${area}: ${parts.join("; ")}`);
    }
    const t = totals(res);
    console.log(`${locale}: ${t.missing} missing, ${t.changed} changed, ${t.broken} broken, ${t.extra} extra`);
    process.exit(bad ? 1 : 0);
} else {
    let bad = false;
    const total = areas().reduce((n, a) => n + Object.keys(read(path.join(LANG, `en-US.${a}.json`))).length, 0);
    for (const locale of SHIPPED) {
        const t = totals(check(locale));
        const done = total - t.missing;
        if (t.broken || t.extra) bad = true;
        console.log(`${locale.padEnd(8)} ${String(Math.round((100 * done) / total)).padStart(3)}% (${done}/${total})  ${t.changed} changed, ${t.broken} broken, ${t.extra} extra`);
    }
    process.exit(bad ? 1 : 0);
}
