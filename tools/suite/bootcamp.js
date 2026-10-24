// The Outgrow boot camp town: irregular streets on six lands, written as the game runner's scenario
// (suite/scenarios/outgrow-bootcamp-make.json: laid, zoned, grown, outgrown and saved as 'PM Bootcamp') and drawn as a map
// to look at before it is built (bootcamp-layout.svg / .png next to the scenario, or --out <dir>).
//   node tools/suite/bootcamp.js [--out <dir>]
// Shapes as the runner's lab op "street" takes them, from each land's middle (x right, z up; yaw in degrees from +x):
// straight {at, yaw, length}, arc {at (centre), radius, from, to}, circle {at, radius}, polygon {points}.
const fs = require('fs');
const path = require('path');
const { execFileSync } = require('child_process');

const Z = 'NA Residential Low';
const lands = [
  { id: 'grid', short: 'crooked grid, odd-angled crossings', about: 'a crooked grid: four streets at odd angles crossing, blocks of every shape', streets: [
    { shape: 'straight', at: [0, 95], yaw: 8, length: 470 },
    { shape: 'straight', at: [0, -85], yaw: -13, length: 470 },
    { shape: 'straight', at: [-120, 0], yaw: 76, length: 470 },
    { shape: 'straight', at: [115, 0], yaw: 104, length: 470 },
  ] },
  { id: 'circles', short: 'ring road, roundabout with 3 spokes', about: 'a ring road and a roundabout with three spokes', streets: [
    { shape: 'circle', at: [-110, -95], radius: 85 },
    { shape: 'circle', at: [110, 115], radius: 22 },
    { shape: 'straight', at: [110 + 75 * Math.cos(20 * Math.PI / 180), 115 + 75 * Math.sin(20 * Math.PI / 180)], yaw: 20, length: 106 },
    { shape: 'straight', at: [110 + 75 * Math.cos(145 * Math.PI / 180), 115 + 75 * Math.sin(145 * Math.PI / 180)], yaw: 145, length: 106 },
    { shape: 'straight', at: [110 + 75 * Math.cos(262 * Math.PI / 180), 115 + 75 * Math.sin(262 * Math.PI / 180)], yaw: 262, length: 106 },
  ] },
  { id: 'curves', short: 'S bend; a curve behind a straight', about: 'an S bend, and a curve backing onto a straight street at an angle', streets: [
    { shape: 'arc', at: [-60, 160], radius: 130, from: 200, to: 290 },
    { shape: 'arc', at: [-60 + 2 * 130 * Math.cos(290 * Math.PI / 180), 160 + 2 * 130 * Math.sin(290 * Math.PI / 180)], radius: 130, from: 110, to: 30 },
    { shape: 'arc', at: [150, -260], radius: 200, from: 95, to: 145 },
    { shape: 'straight', at: [40, -215], yaw: -10, length: 300 },
  ] },
  { id: 'blocks', short: 'big block at 23°, small block at -35°', about: 'a big block turned 23 degrees and a small one turned -35', streets: [
    { shape: 'polygon', points: rect([-70, 40], 250, 210, 23) },
    { shape: 'polygon', points: rect([165, -165], 90, 110, -35) },
  ] },
  { id: 'triangles', short: 'huge triangle, tiny triangle', about: 'a huge triangle and a tiny one', streets: [
    { shape: 'polygon', points: [[-215, -170], [135, -150], [-60, 175]] },
    { shape: 'polygon', points: [[150, 110], [222, 128], [176, 182]] },
  ] },
  { id: 'wedges', short: 'thin wedge; two streets at 18°', about: 'a tiny wedge (a long thin triangle) and two streets meeting at 18 degrees', streets: [
    { shape: 'polygon', points: [[-210, 150], [-30, 180], [-210, 110]] },
    { shape: 'straight', at: [40, -120], yaw: 0, length: 300 },
    { shape: 'straight', at: [40, -120 + 150 * Math.sin(18 * Math.PI / 180) * 1], yaw: 18, length: 300 },
  ] },
];

function rect(c, w, h, deg) {
  const a = deg * Math.PI / 180, cs = Math.cos(a), sn = Math.sin(a);
  return [[-w / 2, -h / 2], [w / 2, -h / 2], [w / 2, h / 2], [-w / 2, h / 2]].map(([x, z]) => [round(c[0] + x * cs - z * sn), round(c[1] + x * sn + z * cs)]);
}
function round(v) { return Math.round(v * 10) / 10; }

// ── the scenario ──
const scenarios = lands.map((l, k) => {
  const steps = [
    { do: 'setting', name: 'NeighbourhoodPolish', value: true },
    { do: 'setting', name: 'PolishNewHouses', value: true },
  ];
  for (const s of l.streets) {
    const st = { do: 'lab', op: 'street', shape: s.shape, zone: Z };
    if (s.at) st.at = s.at.map(round);
    for (const key of ['yaw', 'length', 'radius', 'from', 'to']) if (s[key] !== undefined) st[key] = round(s[key]);
    if (s.points) st.points = s.points;
    steps.push(st);
  }
  steps.push({ do: 'grow', seconds: 150 }, { do: 'lab', op: 'polishIdle', seconds: 120 });
  // (each save holds every land so far: the last, 'PM Bootcamp', holds the whole town)
  if (k === lands.length - 1) steps.push({ do: 'lab', op: 'saveCity', name: 'PM Bootcamp' });
  steps.push({ do: 'shot', name: l.id, camera: { target: 'point', at: [0, 0], view: 'top', distance: 470 }, shotMode: 'render' });
  return { id: l.id, land: { shape: 'rect', size: [520, 520], ground: 'flat', roads: 'none' }, steps };
});
const repo = path.resolve(__dirname, '..', '..');
const scenarioFile = path.join(repo, 'suite', 'scenarios', 'outgrow-bootcamp-make.json');
fs.writeFileSync(scenarioFile, JSON.stringify({
  _about: 'The Outgrow boot camp town (the user, 24 Oct: "a test city with unorthodox angles, almost a random-like grid ... more spaced out than you would think ... circles, roundabouts, curves, straight blocks, big blocks, small blocks, tiny wedges, tiny triangles, huge triangles"). Written by tools/suite/bootcamp.js (edit it there, not here): six lands, ' + lands.map(l => l.id + ' (' + l.about + ')').join(', ') + '. Each land laid, zoned (low density homes), grown with Outgrow on new buildings, pictured from the top; the town saved as \'PM Bootcamp\' after the last. Send with: powershell -File tools/suite/Send-Suite.ps1 -File suite/scenarios/outgrow-bootcamp-make.json -Who outgrow-bootcamp',
  scenarios,
}, null, 2) + '\n');

// ── the map: each land's streets, centre lines and a 40 m band either side where houses grow ──
const outDir = process.argv.includes('--out') ? process.argv[process.argv.indexOf('--out') + 1] : path.dirname(scenarioFile);
const S = 520, G = 30, cols = 3;
const W = cols * (S + G) + G, H = Math.ceil(lands.length / cols) * (S + G + 30) + G;
let svg = `<svg xmlns="http://www.w3.org/2000/svg" width="${W}" height="${H}" viewBox="0 0 ${W} ${H}"><rect width="${W}" height="${H}" fill="#f4f1e8"/>`;
function polyline(s) {
  const pts = [];
  if (s.shape === 'straight') {
    const a = s.yaw * Math.PI / 180, d = [Math.cos(a), Math.sin(a)];
    pts.push([s.at[0] - d[0] * s.length / 2, s.at[1] - d[1] * s.length / 2], [s.at[0] + d[0] * s.length / 2, s.at[1] + d[1] * s.length / 2]);
  } else if (s.shape === 'arc' || s.shape === 'circle') {
    const a0 = s.shape === 'circle' ? 0 : s.from, a1 = s.shape === 'circle' ? 360 : s.to;
    for (let k = 0; k <= 72; k++) { const t = (a0 + (a1 - a0) * k / 72) * Math.PI / 180; pts.push([s.at[0] + s.radius * Math.cos(t), s.at[1] + s.radius * Math.sin(t)]); }
  } else if (s.shape === 'polygon') { pts.push(...s.points, s.points[0]); }
  return pts;
}
lands.forEach((l, k) => {
  const ox = G + (k % cols) * (S + G), oy = G + 30 + Math.floor(k / cols) * (S + G + 30);
  const P = p => `${(ox + S / 2 + p[0]).toFixed(1)},${(oy + S / 2 - p[1]).toFixed(1)}`;
  svg += `<text x="${ox}" y="${oy - 8}" font-family="system-ui" font-size="15"><tspan font-weight="700">${k + 1}. ${l.id}</tspan> — ${l.short}</text>`;
  svg += `<rect x="${ox}" y="${oy}" width="${S}" height="${S}" fill="#dfe8cf" stroke="#999"/>`;
  for (const s of l.streets) svg += `<polyline points="${polyline(s).map(P).join(' ')}" fill="none" stroke="#b9d39a" stroke-width="80" stroke-linejoin="round" stroke-linecap="butt" opacity="0.8"/>`;
  for (const s of l.streets) svg += `<polyline points="${polyline(s).map(P).join(' ')}" fill="none" stroke="#555" stroke-width="7" stroke-linejoin="round"/>`;
  svg += `<line x1="${ox + 10}" y1="${oy + S - 12}" x2="${ox + 110}" y2="${oy + S - 12}" stroke="#333" stroke-width="3"/><text x="${ox + 10}" y="${oy + S - 18}" font-family="system-ui" font-size="12">100 m</text>`;
});
svg += '</svg>';
const svgFile = path.join(outDir, 'bootcamp-layout.svg');
fs.writeFileSync(svgFile, svg);
const edge = [process.env['ProgramFiles(x86)'] + '\\Microsoft\\Edge\\Application\\msedge.exe', process.env.ProgramFiles + '\\Microsoft\\Edge\\Application\\msedge.exe'].find(p => p && fs.existsSync(p));
const png = path.join(outDir, 'bootcamp-layout.png');
if (edge) { try { execFileSync(edge, ['--headless=new', '--disable-gpu', '--hide-scrollbars', `--screenshot=${png}`, `--window-size=${W},${H}`, 'file:///' + svgFile.replace(/\\/g, '/')], { stdio: 'ignore', timeout: 30000 }); } catch (e) { } }
for (let t = 0; edge && t < 50 && !fs.existsSync(png); t++) execFileSync(process.execPath, ['-e', 'setTimeout(()=>{},200)']);
console.log(scenarioFile + '\n' + svgFile + (fs.existsSync(png) ? '\n' + png : ''));
