// Draws a fence map the game runner wrote (lab op fenceMap: <run>/fencemap-NN-<label>.json) as a picture from above:
// roads grey, houses brown, lots dashed, gardens light green with their house's number, fences and hedges as they stand
// (hedges green, fences brown, walls grey; Outgrow's pieces a shade lighter), hidden ones dashed red; gaps magenta rings
// with their length, crossings red crosses, fences inside a garden orange dots, gardens over each other purple.
//
//   node tools/suite/FenceMap.js <fencemap.json> [--at x,z --size m] [--scale px-per-m] [--out file.png]
//
// Without --at: the whole land. Writes an .svg next to the .png (Edge draws the PNG; without Edge only the SVG).
const fs = require('fs');
const path = require('path');
const { execFileSync } = require('child_process');

const args = process.argv.slice(2);
const file = args.find(a => !a.startsWith('--') && !/^-?[\d.,]+$/.test(a));
const opt = name => { const i = args.indexOf('--' + name); return i >= 0 ? args[i + 1] : undefined; };
if (!file) { console.error('usage: node FenceMap.js <fencemap.json> [--at x,z --size m] [--scale px] [--out file.png]'); process.exit(2); }
const d = JSON.parse(fs.readFileSync(file, 'utf8'));
let [lo, hi] = d.box;
if (opt('at')) {
  const [x, z] = opt('at').split(',').map(Number);
  const s = Number(opt('size') || 60) / 2;
  lo = [x - s, z - s]; hi = [x + s, z + s];
}
const span = Math.max(hi[0] - lo[0], hi[1] - lo[1]);
const scale = Number(opt('scale') || Math.min(24, Math.max(2, 2000 / span)));
const W = Math.ceil((hi[0] - lo[0]) * scale), H = Math.ceil((hi[1] - lo[1]) * scale);
// north up: z grows upwards
const X = p => ((p[0] - lo[0]) * scale).toFixed(1);
const Y = p => ((hi[1] - p[1]) * scale).toFixed(1);
const pts = r => r.map(p => X(p) + ',' + Y(p)).join(' ');
const inView = r => r.some(p => p[0] >= lo[0] - 5 && p[0] <= hi[0] + 5 && p[1] >= lo[1] - 5 && p[1] <= hi[1] + 5);
const out = [];
out.push(`<svg xmlns="http://www.w3.org/2000/svg" width="${W}" height="${H}" viewBox="0 0 ${W} ${H}" font-family="Segoe UI, Arial" >`);
out.push(`<rect width="${W}" height="${H}" fill="#dfe8d0"/>`);
for (const r of d.roads) if (inView(r)) out.push(`<polygon points="${pts(r)}" fill="#a9a9a9" stroke="#8c8c8c" stroke-width="0.5"/>`);
for (const g of d.gardens) if (inView(g.ring)) out.push(`<polygon points="${pts(g.ring)}" fill="#9fd37f" fill-opacity="0.45" stroke="#4d8a2f" stroke-width="${Math.max(0.6, scale * 0.08)}"/>`);
for (const o of d.overlaps || []) {
  const a = d.gardens.find(g => g.house === o.a), b = d.gardens.find(g => g.house === o.b);
  for (const g of [a, b]) if (g && inView(g.ring)) out.push(`<polygon points="${pts(g.ring)}" fill="#8a3ccf" fill-opacity="0.18" stroke="#8a3ccf" stroke-width="${Math.max(1, scale * 0.12)}" stroke-dasharray="6 4"/>`);
}
for (const b of d.buildings) {
  if (b.lot && inView(b.lot)) out.push(`<polygon points="${pts(b.lot)}" fill="none" stroke="#7a6a50" stroke-width="0.8" stroke-dasharray="4 3"/>`);
  if (b.body && inView(b.body)) out.push(`<polygon points="${pts(b.body)}" fill="#a0785a" fill-opacity="0.8" stroke="#5b4030" stroke-width="0.8"/>`);
}
for (const r of d.props || []) if (inView(r)) out.push(`<polygon points="${pts(r)}" fill="#c9a27e" stroke="#5b4030" stroke-width="0.6"/>`);
const colour = f => {
  const n = f.name.toLowerCase();
  const base = n.includes('hedge') ? (f.ours ? '#3fae4a' : '#1f6b2a') : n.includes('wall') || n.includes('high0') ? (f.ours ? '#7e8ea8' : '#4a5568') : (f.ours ? '#c48a4a' : '#7a4a1a');
  return base;
};
for (const f of d.fences) {
  if (!inView(f.pts)) continue;
  const w = Math.max(1.2, Math.max(0.25, f.w || 0.3) * scale);
  if (f.hidden) out.push(`<polyline points="${pts(f.pts)}" fill="none" stroke="#e02020" stroke-width="${Math.max(1.5, w * 0.6)}" stroke-dasharray="${scale * 0.5} ${scale * 0.3}" stroke-opacity="0.8"/>`);
  else out.push(`<polyline points="${pts(f.pts)}" fill="none" stroke="${colour(f)}" stroke-width="${w}" stroke-linecap="butt" stroke-opacity="0.9"/>`);
  if (scale >= 10 && !f.hidden) for (const p of [f.pts[0], f.pts[f.pts.length - 1]]) out.push(`<circle cx="${X(p)}" cy="${Y(p)}" r="${Math.max(1, scale * 0.08)}" fill="#000" fill-opacity="0.5"/>`);
}
for (const p of d.openEnds || []) if (inView([p])) out.push(`<circle cx="${X(p)}" cy="${Y(p)}" r="${Math.max(2, scale * 0.25)}" fill="none" stroke="#ff9800" stroke-width="1"/>`);
for (const g of d.gaps) {
  const p = g.at;
  if (!inView([p])) continue;
  const r = Math.max(4, (g.len / 2 + 0.6) * scale);
  out.push(`<circle cx="${X(p)}" cy="${Y(p)}" r="${r}" fill="none" stroke="#e000c0" stroke-width="${Math.max(1.5, scale * 0.12)}"/>`);
  out.push(`<text x="${+X(p) + r + 2}" y="${Y(p)}" font-size="${Math.max(9, scale * 0.9)}" fill="#a00090">${g.kind} ${g.len.toFixed(1)}</text>`);
}
for (const c of d.crossings) {
  const p = c.at;
  if (!inView([p])) continue;
  const r = Math.max(5, scale * 1.2);
  out.push(`<path d="M${+X(p) - r},${+Y(p) - r}L${+X(p) + r},${+Y(p) + r}M${+X(p) - r},${+Y(p) + r}L${+X(p) + r},${+Y(p) - r}" stroke="#e00000" stroke-width="${Math.max(2, scale * 0.15)}"/>`);
}
for (const c of d.inside || []) if (inView([c.at])) out.push(`<circle cx="${X(c.at)}" cy="${Y(c.at)}" r="${Math.max(3, scale * 0.6)}" fill="#ff8c00" fill-opacity="0.85"/>`);
if (scale >= 3) for (const g of d.gardens) {
  if (!inView(g.ring)) continue;
  const c = g.ring.reduce((a, p) => [a[0] + p[0] / g.ring.length, a[1] + p[1] / g.ring.length], [0, 0]);
  out.push(`<text x="${X(c)}" y="${Y(c)}" font-size="${Math.max(8, scale * 1.4)}" fill="#24451a" text-anchor="middle">${g.house}</text>`);
}
// a scale bar and the label
out.push(`<rect x="10" y="${H - 22}" width="${10 * scale}" height="5" fill="#000"/><text x="${14 + 10 * scale}" y="${H - 16}" font-size="12">10 m</text>`);
out.push(`<text x="10" y="18" font-size="14" fill="#000">${d.scenario} ${d.label}: ${d.gaps.length} gaps, ${d.crossings.length} crossings, ${(d.inside || []).length} inside, ${(d.overlaps || []).length} overlaps (${lo.map(v => v.toFixed(0))} to ${hi.map(v => v.toFixed(0))})</text>`);
out.push('</svg>');
const png = path.resolve(opt('out') || file.replace(/\.json$/, '') + (opt('at') ? '-' + opt('at').replace(',', '_') : '') + '.png');
const svg = png.replace(/\.png$/, '.svg');
fs.writeFileSync(svg, out.join('\n'));
const edge = [process.env['ProgramFiles(x86)'] + '\\Microsoft\\Edge\\Application\\msedge.exe', process.env.ProgramFiles + '\\Microsoft\\Edge\\Application\\msedge.exe'].find(p => p && fs.existsSync(p));
if (edge) {
  try { if (fs.existsSync(png)) fs.unlinkSync(png); } catch (e) { }
  try { execFileSync(edge, ['--headless=new', '--disable-gpu', '--hide-scrollbars', `--screenshot=${png}`, `--window-size=${W},${H}`, 'file:///' + svg.replace(/\\/g, '/')], { stdio: 'ignore', timeout: 30000 }); } catch (e) { }
}
// Edge's launcher can return before the picture is written
for (let t = 0; edge && t < 50 && !fs.existsSync(png); t++) execFileSync(process.execPath, ['-e', 'setTimeout(()=>{},200)']);
console.log(fs.existsSync(png) ? png : svg);
