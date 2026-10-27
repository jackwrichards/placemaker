// Builds Lang/en-US.ui.json from the English fallbacks in the code (t("Placemaker.UI.X", "English"))
// plus src/lang/protocol.en.json (keys C# sends as LocText). One source of truth, no drift.
//   node tools/extract-lang.js          write the file
//   node tools/extract-lang.js --check  exit 1 if the file is out of date (used by tests)
const fs = require("fs");
const path = require("path");

const ROOT = path.join(__dirname, "..");
const SRC = path.join(ROOT, "src");
const OUT = path.join(ROOT, "..", "Lang", "en-US.ui.json");

function files(dir, out = []) {
    for (const f of fs.readdirSync(dir)) {
        const p = path.join(dir, f);
        if (fs.statSync(p).isDirectory()) files(p, out);
        else if (/\.tsx?$/.test(f)) out.push(p);
    }
    return out;
}

function unquote(s) {
    return JSON.parse('"' + s.replace(/\\'/g, "'").replace(/"/g, '\\"').replace(/\\\\"/g, '\\"') + '"');
}

function extract() {
    const strings = {};
    // t("Key", "Fallback" …) / t('Key', 'Fallback' …), also fallbacks spanning a newline after the comma.
    const re = /\bt\(\s*(["'])(Placemaker\.UI\.[A-Za-z0-9_.]+)\1\s*,\s*(["'])((?:\\.|(?!\3).)*)\3/g;
    for (const f of files(SRC)) {
        const text = fs.readFileSync(f, "utf8");
        let m;
        while ((m = re.exec(text))) {
            const key = m[2];
            const value = unquote(m[4]);
            if (strings[key] !== undefined && strings[key] !== value)
                console.warn(`extract-lang: ${key} has two different fallbacks:\n  "${strings[key]}"\n  "${value}" (${path.relative(ROOT, f)})`);
            if (strings[key] === undefined) strings[key] = value;
        }
    }
    // A file's own short helper, e.g. const L = (id, en, args) => t("Placemaker.UI.Polish." + id, en, args): its L("Id", "English")
    // calls are that prefix's keys (the Outgrow window's rows were never picked up before, so never translated).
    const helper = /\bconst\s+([A-Za-z_$][\w$]*)\s*=\s*\(\s*([A-Za-z_$][\w$]*)\b[^=]*?\)\s*=>\s*t\(\s*"(Placemaker\.UI\.[A-Za-z0-9_.]*\.)"\s*\+\s*\2\b/g;
    for (const f of files(SRC)) {
        const text = fs.readFileSync(f, "utf8");
        let h;
        while ((h = helper.exec(text))) {
            const call = new RegExp("(?<![\\w$.])" + h[1].replace(/\$/g, "\\$") + "\\(\\s*([\"'])([A-Za-z0-9_.]+)\\1\\s*,\\s*([\"'])((?:\\\\.|(?!\\3).)*)\\3", "g");
            let m;
            while ((m = call.exec(text))) {
                const key = h[3] + m[2];
                const value = unquote(m[4]);
                if (strings[key] !== undefined && strings[key] !== value)
                    console.warn(`extract-lang: ${key} has two different fallbacks:\n  "${strings[key]}"\n  "${value}" (${path.relative(ROOT, f)})`);
                if (strings[key] === undefined) strings[key] = value;
            }
        }
    }
    // Stage names and other keys referenced through tables.
    const tables = /\{\s*id:\s*"[a-z]+",\s*key:\s*"(Placemaker\.UI\.[A-Za-z.]+)",\s*en:\s*"([^"]+)"\s*\}/g;
    for (const f of files(SRC)) {
        const text = fs.readFileSync(f, "utf8");
        let m;
        while ((m = tables.exec(text))) if (strings[m[1]] === undefined) strings[m[1]] = m[2];
    }
    // ["Placemaker.UI.Key", "English"] pairs in lookup tables.
    const pairs = /\[\s*"(Placemaker\.UI\.[A-Za-z0-9_.]+)"\s*,\s*"((?:\\.|[^"\\])*)"\s*\]/g;
    for (const f of files(SRC)) {
        const text = fs.readFileSync(f, "utf8");
        let m;
        while ((m = pairs.exec(text))) if (strings[m[1]] === undefined) strings[m[1]] = unquote(m[2]);
    }
    const protocol = JSON.parse(fs.readFileSync(path.join(SRC, "lang", "protocol.en.json"), "utf8"));
    for (const [k, v] of Object.entries(protocol)) if (!k.startsWith("_")) strings[k] = v;
    const sorted = {};
    for (const k of Object.keys(strings).sort()) sorted[k] = strings[k];
    return JSON.stringify(sorted, null, 2) + "\n";
}

const next = extract();
const cur = fs.existsSync(OUT) ? fs.readFileSync(OUT, "utf8").replace(/\r\n/g, "\n") : "";
if (process.argv.includes("--check")) {
    if (cur !== next) {
        console.error("Lang/en-US.ui.json is out of date: run `npm run lang`.");
        process.exit(1);
    }
    console.log("Lang/en-US.ui.json is up to date (" + Object.keys(JSON.parse(next)).length + " keys).");
} else if (cur === next) {
    // unchanged: not rewritten (a rewrite turns the checkout's CRLF into LF and leaves the tree "modified" after every build)
    console.log("Lang/en-US.ui.json is up to date (" + Object.keys(JSON.parse(next)).length + " keys).");
} else {
    fs.writeFileSync(OUT, next);
    console.log("Wrote " + path.relative(process.cwd(), OUT) + " (" + Object.keys(JSON.parse(next)).length + " keys).");
}
