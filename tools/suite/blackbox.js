// The Outgrow black-box towns: random streets and random housing from a secret seed, built once in the game and recorded, for
// scoring a planner on towns it was never tuned on (the user, 24 Oct: "a random shape with a random housing that it can't
// account for ... a black box to the agent"). The scenario and the recordings stay outside the worktrees.
//   node tools/suite/blackbox.js <seed> [--towns 2] [--out <dir>]   writes <dir>/blackbox.json (default .forensics/blackbox)
// Each town: one 520 m land, 3-5 features drawn at random (straight streets at any angle, crossing or not; a block of 4-6
// corners; a triangle; an arc; a ring), each zoned with a random housing (mostly low density homes, some row houses, a few
// shops), grown, every garden planned again twice, the second round recorded (passes/bbN) and measured (gardenReport bbN).
const fs = require('fs');
const path = require('path');

const seed = Number(process.argv[2] || 1);
const towns = process.argv.includes('--towns') ? Number(process.argv[process.argv.indexOf('--towns') + 1]) : 2;
const repo = path.resolve(__dirname, '..', '..');
// (the main checkout's .forensics, also from a worktree)
const main = repo.includes(path.join('.claude', 'worktrees')) ? repo.slice(0, repo.indexOf(path.join('.claude', 'worktrees'))) : repo + path.sep;
const outDir = process.argv.includes('--out') ? process.argv[process.argv.indexOf('--out') + 1] : path.join(main, '.forensics', 'blackbox');

let s = seed >>> 0 || 1;
const rnd = () => { s ^= s << 13; s >>>= 0; s ^= s >>> 17; s ^= s << 5; s >>>= 0; return s / 4294967296; };
const pick = a => a[Math.floor(rnd() * a.length)];
const between = (a, b) => a + (b - a) * rnd();
const round = v => Math.round(v * 10) / 10;
const zones = ['NA Residential Low', 'NA Residential Low', 'NA Residential Low', 'EU Residential Low', 'NA Residential Medium Row', 'NA Commercial Low'];

function feature() {
  const kind = pick(['straight', 'straight', 'cross', 'block', 'triangle', 'arc', 'ring']);
  const c = [between(-150, 150), between(-150, 150)];
  const zone = pick(zones);
  if (kind === 'straight') return [{ shape: 'straight', at: c, yaw: between(0, 180), length: between(180, 420), zone }];
  if (kind === 'cross') { const y = between(0, 180); return [{ shape: 'straight', at: c, yaw: y, length: between(220, 420), zone }, { shape: 'straight', at: c, yaw: y + between(25, 155), length: between(180, 380), zone }]; }
  if (kind === 'block' || kind === 'triangle') {
    const n = kind === 'triangle' ? 3 : 4 + Math.floor(rnd() * 3), r = between(60, 170), a0 = between(0, 360);
    const pts = [];
    for (let k = 0; k < n; k++) { const a = (a0 + 360 * k / n + between(-18, 18)) * Math.PI / 180, rr = r * between(0.75, 1.2); pts.push([round(c[0] + rr * Math.cos(a)), round(c[1] + rr * Math.sin(a))]); }
    return [{ shape: 'polygon', points: pts, zone }];
  }
  if (kind === 'arc') { const f = between(0, 360); return [{ shape: 'arc', at: c, radius: between(80, 220), from: f, to: f + between(50, 140), zone }]; }
  return [{ shape: 'circle', at: c, radius: between(35, 110), zone }];
}

const scenarios = [];
for (let t = 1; t <= towns; t++) {
  const label = 'bb' + t;
  const streets = [];
  const n = 3 + Math.floor(rnd() * 3);
  for (let k = 0; k < n; k++) streets.push(...feature());
  const steps = [{ do: 'setting', name: 'NeighbourhoodPolish', value: true }, { do: 'setting', name: 'PolishNewHouses', value: true }];
  for (const st of streets) {
    const step = { do: 'lab', op: 'street', shape: st.shape, zone: st.zone };
    for (const k of ['at', 'points']) if (st[k]) step[k] = k === 'at' ? st[k].map(round) : st[k];
    for (const k of ['yaw', 'length', 'radius', 'from', 'to']) if (st[k] !== undefined) step[k] = round(st[k]);
    steps.push(step);
  }
  steps.push({ do: 'grow', seconds: 150 }, { do: 'lab', op: 'polishIdle', seconds: 120 });
  steps.push({ do: 'lab', op: 'polishSettings', settings: {} }, { do: 'lab', op: 'polishIdle', seconds: 120 });
  steps.push({ do: 'lab', op: 'polishPassDump', max: 600, label });
  steps.push({ do: 'lab', op: 'polishSettings', settings: {} }, { do: 'lab', op: 'polishIdle', seconds: 120 });
  steps.push({ do: 'lab', op: 'gardenReport', label, max: 8, rel: true, places: [{ name: label, at: [0, 0], size: 520 }] });
  steps.push({ do: 'lab', op: 'polishPassDump', max: 0, label: 'off' });
  scenarios.push({ id: label, land: { shape: 'rect', size: [520, 520], ground: 'flat', roads: 'none' }, steps });
}
fs.mkdirSync(outDir, { recursive: true });
const file = path.join(outDir, 'blackbox.json');
fs.writeFileSync(file, JSON.stringify({ _about: 'The Outgrow black-box towns (tools/suite/blackbox.js, a secret seed): random streets and housing, built, grown, planned again twice, the second round recorded and measured. Not for the agents to read.', scenarios }, null, 2) + '\n');
console.log(file);
