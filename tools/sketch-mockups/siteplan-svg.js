// tools/sketch-mockups/siteplan-svg.js — draws a site plan as the panel's top view receives it (C# Core/Build/SitePlan.cs ToUi,
// written by the gallery's --plan-json as <variant>.siteplan.json), in the same colours as UI/src/components/SitePlanCard.tsx, for
// offline checks across every type.
//   node siteplan-svg.js <gallery dir> <out.html> [width]   → a contact sheet: one picture per <type>/<variant>.siteplan.json
const fs = require('fs'), path = require('path');

const LAND = '#6f8a4c', ASPHALT = '#474b52';
const f1 = (v) => (Math.round(v * 10) / 10).toString();
const P = (x, z) => f1(x) + ',' + f1(-z);
const ringD = (a) => { if (a.length < 6) return ''; let d = 'M' + P(a[0], a[1]); for (let i = 2; i + 1 < a.length; i += 2) d += 'L' + P(a[i], a[i + 1]); return d + 'Z'; };
const lineD = (a) => { if (a.length < 4) return ''; let d = 'M' + P(a[0], a[1]); for (let i = 2; i + 1 < a.length; i += 2) d += 'L' + P(a[i], a[i + 1]); return d; };
const circlesD = (xyr, rOf, dx = 0, dz = 0) => {
  let d = '';
  for (let i = 0; i + 2 < xyr.length; i += 3) {
    const r = rOf(xyr[i + 2]), x = xyr[i] + dx * xyr[i + 2], z = xyr[i + 1] + dz * xyr[i + 2];
    d += 'M' + f1(x - r) + ',' + f1(-z) + 'a' + f1(r) + ',' + f1(r) + ' 0 1,0 ' + f1(2 * r) + ',0a' + f1(r) + ',' + f1(r) + ' 0 1,0 ' + f1(-2 * r) + ',0';
  }
  return d;
};
function over(hex, alpha, base = LAND) {
  const p = (h) => [1, 3, 5].map((i) => parseInt(h.substr(i, 2), 16));
  const a = p(hex), b = p(base);
  return '#' + a.map((v, i) => Math.round(v * alpha + b[i] * (1 - alpha)).toString(16).padStart(2, '0')).join('');
}

function svg(sp, W) {
  const span = Math.max(sp.max[0] - sp.min[0], sp.max[1] - sp.min[1], 10), pad = Math.max(6, span * 0.06);
  const vx = sp.min[0] - pad, vy = -sp.max[1] - pad, vw = sp.max[0] - sp.min[0] + 2 * pad, vh = sp.max[1] - sp.min[1] + 2 * pad;
  const H = Math.round(W * vh / vw), ppm = W / vw, px = (n) => n / ppm;
  const o = [`<svg xmlns="http://www.w3.org/2000/svg" viewBox="${f1(vx)} ${f1(vy)} ${f1(vw)} ${f1(vh)}" width="${W}" height="${H}"><rect x="${vx}" y="${vy}" width="${vw}" height="${vh}" fill="${LAND}"/>`];
  const outline = ringD(sp.outline);
  for (const s of sp.streets || []) o.push(`<path d="${lineD(s.p)}" stroke="#8d9196" stroke-width="${s.w}" fill="none"/>`);
  o.push(`<path d="${outline}" fill="${over('#9cc76f', 0.28)}"/>`);
  for (const f of sp.fills) o.push(`<path d="${f.rings.map(ringD).join('')}" fill="${over(f.color, f.alpha)}"/>`);
  for (const p of sp.plots) o.push(`<path d="${ringD(p.q)}" fill="${over(p.color, 0.55)}" stroke="#fff" stroke-opacity="0.5" stroke-width="${px(0.6)}"/>`);
  for (const h of sp.heat || []) o.push(`<path d="${h.rings.map(ringD).join('')}" fill="${h.color}" fill-opacity="${h.alpha}"/>`);
  const asphalt = over(ASPHALT, 0.92);
  for (const c of sp.cars) o.push(`<path d="${lineD(c.p)}" stroke="${asphalt}" stroke-width="${c.w}" fill="none" stroke-linecap="round" stroke-linejoin="round"/>`);
  if (sp.caps.length) o.push(`<path d="${circlesD(sp.caps, (r) => r)}" fill="${asphalt}"/>`);
  for (const c of sp.cars.filter((c) => c.mark)) o.push(`<path d="${lineD(c.p)}" stroke="#f3f1e8" stroke-width="${Math.max(0.3, px(0.8))}" stroke-dasharray="4 4" fill="none"/>`);
  for (const w of sp.walks) o.push(`<path d="${lineD(w.p)}" stroke="#eadfc2" stroke-width="${w.w}" fill="none" stroke-linecap="round" stroke-linejoin="round"/>`);
  if (sp.fences.length) o.push(`<path d="${sp.fences.map(lineD).join('')}" stroke="#6b5440" stroke-width="${Math.max(0.35, px(0.8))}" fill="none"/>`);
  if (sp.stalls.length) { let d = ''; for (let i = 0; i + 3 < sp.stalls.length; i += 4) d += 'M' + P(sp.stalls[i], sp.stalls[i + 1]) + 'L' + P(sp.stalls[i + 2], sp.stalls[i + 3]); o.push(`<path d="${d}" stroke="#fff" stroke-width="${Math.max(0.18, px(0.7))}" fill="none"/>`); }
  if (sp.propShadows.length) o.push(`<path d="${sp.propShadows.map(ringD).join('')}" fill="#08140a" fill-opacity="0.28"/>`);
  for (const g of sp.props) {
    if (g.rings.length) o.push(`<path d="${g.rings.map(ringD).join('')}" fill="${g.color}" stroke="#1e1f1d" stroke-opacity="0.45" stroke-width="${Math.max(0.12, px(0.5))}"/>`);
    if (g.discs.length) o.push(`<path d="${circlesD(g.discs, (r) => Math.max(r, px(0.9)))}" fill="${g.color}"/>`);
  }
  if (sp.bushes.length) o.push(`<path d="${circlesD(sp.bushes, (r) => Math.max(r, px(1)))}" fill="${over('#5f9e48', 0.9)}"/>`);
  if (sp.trees.length) {
    const rr = (r) => Math.max(r, px(1.6));
    o.push(`<path d="${circlesD(sp.trees, rr, 0.25, -0.3)}" fill="#08140a" fill-opacity="0.28"/>`, `<path d="${circlesD(sp.trees, rr)}" fill="#3b7533"/>`, `<path d="${circlesD(sp.trees, (r) => rr(r) * 0.55, -0.22, 0.22)}" fill="#5a9a44"/>`);
  }
  o.push(`<path d="${outline}" fill="none" stroke="#d6ecff" stroke-width="${px(1.3)}"/></svg>`);
  return o.join('');
}

if (require.main === module) {
  const [dir, out, w] = process.argv.slice(2);
  const W = +(w || 420);
  const cells = [];
  for (const t of fs.readdirSync(dir).sort()) {
    const d = path.join(dir, t);
    if (!fs.statSync(d).isDirectory()) continue;
    for (const f of fs.readdirSync(d).filter((x) => x.endsWith('.siteplan.json')).sort()) {
      const sp = JSON.parse(fs.readFileSync(path.join(d, f), 'utf8'));
      const rows = (sp.legend || []).map((r) => r.en + (r.count ? ' ' + r.count : '')).join(' · ');
      cells.push(`<div class="c"><div class="t">${t} / ${f.replace('.siteplan.json', '')}</div>${svg(sp, W)}<div class="l">${rows}</div></div>`);
    }
  }
  fs.writeFileSync(out, `<!doctype html><meta charset="utf-8"><style>body{margin:0;background:#141a21;color:#dfe7ef;font:12px Segoe UI,Arial}.g{display:flex;flex-wrap:wrap}.c{width:${W + 16}px;padding:8px;box-sizing:border-box}.t{font-weight:600;margin-bottom:4px}.l{color:#93a1af;font-size:11px;margin-top:3px}</style><div class="g">${cells.join('')}</div>`);
  console.log(cells.length + ' pictures → ' + out);
}

module.exports = { svg };
