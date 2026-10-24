// Draws the garden report the game runner wrote (lab op gardenReport: gardens-<way>.json, coverage-<way>.json in the queue
// game's run folder) one picture per place, and prints the breakdown for the places alone:
//   node tools/suite/GardenMap.js <folder with the reports> <way> [--out <dir>]
// A picture: roads grey; the land Outgrow should cover light green where a garden covers it, RED where none does; every
// garden's outline (green: nothing flagged, orange: flagged, dark red: among the worst), its edge count on it; its corners:
// a red dot under 45 or over 315 degrees (a spike, a deep notch), a purple dot within 15 of straight (a slight bend).
const fs = require('fs');
const path = require('path');
const { execFileSync } = require('child_process');

const dir = process.argv[2], way = process.argv[3] || 'now';
const outDir = process.argv.includes('--out') ? process.argv[process.argv.indexOf('--out') + 1] : dir;
const gardens = JSON.parse(fs.readFileSync(path.join(dir, `gardens-${way}.json`), 'utf8')).gardens;
const covers = JSON.parse(fs.readFileSync(path.join(dir, `coverage-${way}.json`), 'utf8'));
const edge = [process.env['ProgramFiles(x86)'] + '\\Microsoft\\Edge\\Application\\msedge.exe', process.env.ProgramFiles + '\\Microsoft\\Edge\\Application\\msedge.exe'].find(p => p && fs.existsSync(p));

// ── the breakdown, the places only ──
const placed = gardens.filter(g => g.place);
const out = [];
const P = (s) => out.push(s);
P(`${way}: ${placed.length} gardens in ${covers.length} places (${gardens.length - placed.length} elsewhere left out)`);
P('coverage of the land Outgrow should cover (within 40 m of a road and 20 m of a lot, and what that encloses):');
let e = 0, c = 0;
for (const cv of covers) { e += cv.expected; c += cv.covered; P(`  ${cv.place.padEnd(10)} ${cv.percent.toFixed(1).padStart(5)}%   ${((cv.expected - cv.covered)).toLocaleString()} m² uncovered of ${cv.expected.toLocaleString()} m²`); }
P(`  ${'all'.padEnd(10)} ${(100 * c / e).toFixed(1).padStart(5)}%`);
const edges = placed.map(g => g.edges).sort((a, b) => a - b);
P(`edges per garden (a shared edge counts for every garden that has it): mean ${(edges.reduce((a, b) => a + b, 0) / edges.length).toFixed(1)}, median ${edges[edges.length >> 1]}, most ${edges[edges.length - 1]}`);
const hist = {}; edges.forEach(n => hist[n] = (hist[n] || 0) + 1);
P('  gardens with n edges: ' + Object.keys(hist).map(Number).sort((a, b) => a - b).map(n => `${n}:${hist[n]}`).join('  '));
P(`  4 edges (a plain four-sided garden): ${hist[4] || 0}; 5-6: ${(hist[5] || 0) + (hist[6] || 0)}; 7-8: ${(hist[7] || 0) + (hist[8] || 0)}; over 8: ${edges.filter(n => n > 8).length}`);
const all = placed.flatMap(g => g.edgeList);
P(`shared edges: ${all.filter(x => x.sharedWith.length).length} of ${all.length}; per garden ${(all.filter(x => x.sharedWith.length).length / placed.length).toFixed(1)} shared, ${(all.filter(x => !x.sharedWith.length).length / placed.length).toFixed(1)} its own`);
P(`edges under 2 m: ${all.filter(x => x.length < 2).length} (${(100 * all.filter(x => x.length < 2).length / all.length).toFixed(0)}% of all edges), under 4 m: ${all.filter(x => x.length < 4).length}`);
const ang = placed.flatMap(g => g.corners.map(k => k.angle));
const bins = [['spike <45', 0, 45], ['sharp 45-75', 45, 75], ['square 75-105', 75, 105], ['wide 105-165', 105, 165], ['slight bend out 165-178', 165, 178], ['slight bend in 182-195', 182, 195], ['wide in 195-255', 195, 255], ['square in 255-285', 255, 285], ['sharp in 285-315', 285, 315], ['deep notch >315', 315, 361]];
P(`corner angles (inside, ${ang.length} corners):`);
for (const [n, lo, hi] of bins) { const k = ang.filter(a => a >= lo && a < hi).length; P(`  ${n.padEnd(24)} ${String(k).padStart(5)}  ${'#'.repeat(Math.round(60 * k / ang.length))}`); }
// the angle runs that repeat across gardens (a shape the planner makes again and again)
const runs = {};
for (const g of placed) {
  const a = g.corners.map(k => Math.round(k.angle / 3) * 3);
  for (let i = 0; i < a.length; i++) { const key = [0, 1, 2, 3].map(j => a[(i + j) % a.length]).join(' '); runs[key] = (runs[key] || new Set()).add(g.id); }
}
P('the corner runs most gardens share (4 corners in a row, angles to 3 degrees: one shape made over and over):');
Object.entries(runs).filter(([k, s]) => !/^(9[0-9] ){3}9[0-9]$/.test(k)).sort((a, b) => b[1].size - a[1].size).slice(0, 6).forEach(([k, s]) => P(`  ${k.padEnd(20)} in ${s.size} gardens`));
P('by place: gardens, edges mean/most, flagged, spikes, deep notches, slight bends, edges under 2 m');
const places = [...new Set(placed.map(g => g.place))];
for (const pl of places) {
  const gs = placed.filter(g => g.place === pl);
  const cs = gs.flatMap(g => g.corners.map(k => k.angle));
  P(`  ${pl.padEnd(10)} ${String(gs.length).padStart(3)}, ${(gs.reduce((a, g) => a + g.edges, 0) / gs.length).toFixed(1)}/${Math.max(...gs.map(g => g.edges))}, ${gs.filter(g => g.flags.length).length}, ${cs.filter(a => a < 45).length}, ${cs.filter(a => a > 315).length}, ${cs.filter(a => Math.abs(180 - a) < 15).length}, ${gs.flatMap(g => g.edgeList).filter(x => x.length < 2).length}`);
}
const worst = [...placed].sort((a, b) => b.score - a.score);
const worstIds = new Set(worst.slice(0, Math.max(10, Math.round(placed.length * 0.05))).map(g => g.id));
P(`flagged: ${placed.filter(g => g.flags.length).length} of ${placed.length}`);
console.log(out.join('\n'));
fs.writeFileSync(path.join(outDir, `garden-breakdown-${way}.txt`), out.join('\n') + '\n');

// ── the pictures ──
const pngs = [];
for (const cv of covers) {
  const S = 2, W = cv.w * S / cv.cell * cv.cell, H = cv.h * S;
  const X = x => ((x - cv.origin[0]) * S).toFixed(1), Y = z => (H - (z - cv.origin[1]) * S).toFixed(1);
  let svg = `<svg xmlns="http://www.w3.org/2000/svg" width="${cv.w * S}" height="${H}" viewBox="0 0 ${cv.w * S} ${H}"><rect width="100%" height="100%" fill="#f6f4ee"/>`;
  // the mask, row by row in runs
  for (let y = 0; y < cv.h; y++) {
    const row = cv.mask.substr(y * cv.w, cv.w);
    let x = 0;
    while (x < cv.w) {
      const ch = row[x];
      let x2 = x;
      while (x2 < cv.w && row[x2] === ch) x2++;
      if (ch !== '.') {
        const fill = ch === 'r' ? '#a9a9a9' : ch === 'c' ? '#cfe3b4' : '#ff3030';
        svg += `<rect x="${x * S}" y="${H - (y + 1) * S}" width="${(x2 - x) * S}" height="${S}" fill="${fill}"/>`;
      }
      x = x2;
    }
  }
  const lo = cv.origin, hi = [cv.origin[0] + cv.w * cv.cell, cv.origin[1] + cv.h * cv.cell];
  const mine = placed.filter(g => g.place === cv.place);
  for (const g of mine) {
    const col = worstIds.has(g.id) ? '#8b0000' : g.flags.length ? '#e07b00' : '#2e7d32';
    svg += `<polygon points="${g.corners.map(k => X(k.at[0]) + ',' + Y(k.at[1])).join(' ')}" fill="none" stroke="${col}" stroke-width="${worstIds.has(g.id) ? 2.2 : 1.3}"/>`;
    for (const k of g.corners) {
      if (k.angle < 45 || k.angle > 315) svg += `<circle cx="${X(k.at[0])}" cy="${Y(k.at[1])}" r="4" fill="#e00000"/>`;
      else if (Math.abs(180 - k.angle) < 15) svg += `<circle cx="${X(k.at[0])}" cy="${Y(k.at[1])}" r="3" fill="#8e24aa"/>`;
    }
  }
  for (const g of mine) svg += `<text x="${X(g.at[0])}" y="${Y(g.at[1])}" font-family="system-ui" font-size="13" font-weight="700" text-anchor="middle" fill="${worstIds.has(g.id) ? '#8b0000' : g.edges > 8 ? '#b35900' : '#1b5e20'}" stroke="#fff" stroke-width="3" paint-order="stroke">${g.edges}</text>`;
  svg += `<text x="10" y="24" font-family="system-ui" font-size="20" font-weight="700" stroke="#fff" stroke-width="4" paint-order="stroke">${cv.place}: ${cv.percent.toFixed(1)}% covered, ${mine.length} gardens, ${(mine.reduce((a, g) => a + g.edges, 0) / Math.max(1, mine.length)).toFixed(1)} edges on average</text>`;
  svg += '</svg>';
  const f = path.join(outDir, `garden-map-${way}-${cv.place}.svg`);
  fs.writeFileSync(f, svg);
  const png = f.replace(/\.svg$/, '.png');
  if (edge) {
    try { fs.unlinkSync(png); } catch (x) { }
    try { execFileSync(edge, ['--headless=new', '--disable-gpu', '--hide-scrollbars', `--screenshot=${png}`, `--window-size=${cv.w * S},${H}`, 'file:///' + f.replace(/\\/g, '/')], { stdio: 'ignore', timeout: 60000 }); } catch (x) { }
    for (let t = 0; t < 50 && !fs.existsSync(png); t++) execFileSync(process.execPath, ['-e', 'setTimeout(()=>{},200)']);
  }
  pngs.push(png);
}
console.log(pngs.join('\n'));
