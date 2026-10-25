// The game's own words, per language, for the translators: reads the game's Locale.cok (a zip of <locale>.loc files) and
// writes, for every locale, the game's short texts (1-4 words) as English -> that language, keeping only the ones a word of
// our English uses (so "Zoning", "Parking lot", "Pedestrian path" read the way the player already knows them).
//
//   node tools/lang/glossary.js [out dir]   (default: <main checkout>/.forensics/lang/glossary; git-ignored: the game's text)
//
// .loc: a .NET BinaryWriter stream: ushort version, string name, string locale id, string native name, int32 count, then
// count pairs (key, value) of 7-bit length-prefixed UTF-8 strings (Colossal.Localization LocaleAsset).
const fs = require("fs");
const path = require("path");
const zlib = require("zlib");

const REPO = path.resolve(__dirname, "..", "..");
const GAME = "C:/Program Files (x86)/Steam/steamapps/common/Cities Skylines II/Cities2_Data/Content/Game/Locale.cok";
const OUT = process.argv[2] || path.join("C:/Users/jack/cs2-area-tool/.forensics/lang/glossary");

/** The files of a zip (stored or deflated), by name. */
function unzip(buf) {
    const files = {};
    let eocd = buf.length - 22;
    while (eocd >= 0 && buf.readUInt32LE(eocd) !== 0x06054b50) eocd--;
    const count = buf.readUInt16LE(eocd + 10);
    let p = buf.readUInt32LE(eocd + 16);
    for (let i = 0; i < count; i++) {
        const method = buf.readUInt16LE(p + 10);
        const size = buf.readUInt32LE(p + 20);
        const nameLen = buf.readUInt16LE(p + 28), extraLen = buf.readUInt16LE(p + 30), commentLen = buf.readUInt16LE(p + 32);
        const local = buf.readUInt32LE(p + 42);
        const name = buf.toString("utf8", p + 46, p + 46 + nameLen);
        const dataAt = local + 30 + buf.readUInt16LE(local + 26) + buf.readUInt16LE(local + 28);
        const raw = buf.subarray(dataAt, dataAt + size);
        files[name] = method === 0 ? raw : zlib.inflateRawSync(raw);
        p += 46 + nameLen + extraLen + commentLen;
    }
    return files;
}

function readLoc(buf) {
    let p = 0;
    const str = () => {
        let len = 0, shift = 0, b;
        do {
            b = buf[p++];
            len |= (b & 0x7f) << shift;
            shift += 7;
        } while (b & 0x80);
        const s = buf.toString("utf8", p, p + len);
        p += len;
        return s;
    };
    p += 2; // version
    const name = str(), id = str(), native = str();
    const count = buf.readInt32LE(p);
    p += 4;
    const entries = {};
    for (let i = 0; i < count; i++) {
        const k = str();
        entries[k] = str();
    }
    return { name, id, native, entries };
}

/** Every word of our English (Lang/en-US.*.json), lower case. */
function ourWords() {
    const words = new Set();
    for (const f of fs.readdirSync(path.join(REPO, "Lang")).filter((f) => f.startsWith("en-US."))) {
        const j = JSON.parse(fs.readFileSync(path.join(REPO, "Lang", f), "utf8"));
        for (const v of Object.values(j)) for (const w of String(v).toLowerCase().match(/[a-z][a-z'-]+/g) ?? []) words.add(w);
    }
    return words;
}

const files = unzip(fs.readFileSync(GAME));
const locs = {};
for (const [name, buf] of Object.entries(files)) if (name.endsWith(".loc")) {
    const l = readLoc(buf);
    locs[l.id] = l;
}
const en = locs["en-US"].entries;
const words = ourWords();
fs.mkdirSync(OUT, { recursive: true });
for (const [id, l] of Object.entries(locs)) {
    if (id === "en-US") continue;
    // English short text -> its most common translation in this locale
    const votes = new Map();
    for (const [k, v] of Object.entries(en)) {
        const t = l.entries[k];
        if (!t || !v || /[{}<>\n]/.test(v)) continue;
        const n = v.trim().split(/\s+/).length;
        if (n > 4 || v.length > 40) continue;
        const ws = v.toLowerCase().match(/[a-z][a-z'-]+/g) ?? [];
        if (!ws.length || !ws.every((w) => words.has(w))) continue;
        const key = v.trim();
        const m = votes.get(key) ?? new Map();
        m.set(t.trim(), (m.get(t.trim()) ?? 0) + 1);
        votes.set(key, m);
    }
    const out = {};
    for (const [eng, m] of [...votes.entries()].sort((a, b) => a[0].localeCompare(b[0]))) {
        out[eng] = [...m.entries()].sort((a, b) => b[1] - a[1])[0][0];
    }
    fs.writeFileSync(path.join(OUT, id + ".json"), JSON.stringify({ _about: `The game's own words, English -> ${l.name} (${l.native}), from Locale.cok`, terms: out }, null, 1));
    console.log(id, l.native, Object.keys(l.entries).length, "game texts,", Object.keys(out).length, "glossary terms");
}
