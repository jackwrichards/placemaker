// tools/sketch-mockups/render.js — sketch mock-ups (docs/sketch-study): draws a Placemaker plan (Gallery --plan-json) as SVG in proposed sketch styles.
//   node render.js <plan.json> <style: site|line> [--w 900] [--hide layer,layer] [--only layer] > out.svg
// Also a module: render(plan, style, opts) -> svg; legendRows(plan) -> rows; swatch(sym, style) -> svg.


const BASE = {
  ground: '#6f8a4c', street: '#8d9196', kerb: '#c9ccd0',
  outline: '#d6ecff', accent: '#4fa3ff',
  zone: { commercialLow: '#4f8fe6', commercialHigh: '#2f6fd0', residentialLow: '#5cc25c', officeLow: '#a77fe6', industrial: '#e6c64f' },
};
const STYLES = {
  // A. Site plan: what gets built, at its real size, in real-world colours. Fills only, no outlines.
  site: Object.assign({}, BASE, {
    name: 'Site plan',
    asphalt: '#474b52', asphaltA: 0.85, road: '#474b52', roadA: 0.9, roadMark: '#f3f1e8',
    path: '#eadfc2', pathA: 0.95,
    grass: '#8fd16a', grassA: 0.28, paving: '#e6d8b8', pavingA: 0.75, plaza: '#efd9a8', plazaA: 0.8,
    water: '#3f86d8', waterA: 0.8, deco: '#e3c99a', decoA: 0.7, groundSurf: '#b39a6c', groundSurfA: 0.3,
    tree: '#3b7533', treeHi: '#5a9a44', shadow: 'rgba(8,20,8,0.28)', bush: '#5f9e48', bushA: 0.85,
    feature: '#f0a868', featureEdge: '#a95f24', furniture: '#26272a', lamp: '#fff3c4', bench: '#7a5a3a',
    bay: '#ffffff', siteTint: ['#ffffff', 0.05],
  }),
  // B. Line drawing: the area dimmed, everything in white linework at real size; colour only where it means something.
  line: Object.assign({}, BASE, {
    name: 'Line drawing',
    line: true, siteTint: ['#0b1622', 0.45],
    asphalt: '#ffffff', asphaltA: 0.10, road: '#ffffff', roadA: 0.16, roadMark: '#ffffff', roadEdge: '#ffffff',
    path: '#ffe9b0', pathA: 0.55,
    grass: '#8fd16a', grassA: 0.18, paving: '#ffe9b0', pavingA: 0.18, plaza: '#ffe9b0', plazaA: 0.2,
    water: '#5aa8ff', waterA: 0.45, deco: '#ffe9b0', decoA: 0.15, groundSurf: '#ffffff', groundSurfA: 0.05,
    tree: '#9be37a', treeHi: null, shadow: null, bush: '#9be37a', bushA: 0.6,
    feature: '#ffb36b', featureEdge: '#ffb36b', furniture: '#ffffff', lamp: '#fff3c4', bench: '#ffffff',
    bay: '#ffffff',
  }),
};

function bounds(pts) {
  let x0 = Infinity, y0 = Infinity, x1 = -Infinity, y1 = -Infinity;
  for (const [x, y] of pts) { x0 = Math.min(x0, x); y0 = Math.min(y0, y); x1 = Math.max(x1, x); y1 = Math.max(y1, y); }
  return { x0, y0, x1, y1 };
}
const f = (v) => +v.toFixed(2);
const pt = ([x, z]) => `${f(x)},${f(-z)}`;
const poly = (r) => r.map(pt).join(' ');
const pathD = (pts) => 'M' + pts.map(pt).join(' L');
const d2 = (a, b) => Math.hypot(a[0] - b[0], a[1] - b[1]);

function zoneColor(S, id) {
  id = (id || '').toLowerCase();
  if (id.includes('office')) return S.zone.officeLow;
  if (id.includes('industr')) return S.zone.industrial;
  if (id.includes('resid') || id.includes('row') || id.includes('rent')) return S.zone.residentialLow;
  if (id.includes('high')) return S.zone.commercialHigh;
  return S.zone.commercialLow;
}

// Stall lines as painted: the two long sides (separators) and the back line; the aisle side stays open.
function bayLines(p, aisles) {
  const long01 = d2(p[0], p[1]) >= d2(p[1], p[2]);
  const sides = long01 ? [[p[0], p[1]], [p[3], p[2]]] : [[p[1], p[2]], [p[0], p[3]]];
  const shorts = long01 ? [[p[1], p[2]], [p[3], p[0]]] : [[p[0], p[1]], [p[2], p[3]]];
  const mid = (s) => [(s[0][0] + s[1][0]) / 2, (s[0][1] + s[1][1]) / 2];
  const near = (m) => { let best = Infinity; for (const a of aisles) for (let i = 1; i < a.length; i++) best = Math.min(best, segDist(m, a[i - 1], a[i])); return best; };
  const back = near(mid(shorts[0])) > near(mid(shorts[1])) ? shorts[0] : shorts[1];
  return [...sides, back];
}
function segDist(p, a, b) {
  const dx = b[0] - a[0], dy = b[1] - a[1], l = dx * dx + dy * dy;
  const t = l < 1e-9 ? 0 : Math.max(0, Math.min(1, ((p[0] - a[0]) * dx + (p[1] - a[1]) * dy) / l));
  return Math.hypot(p[0] - a[0] - t * dx, p[1] - a[1] - t * dy);
}

function render(plan, style, opts = {}) {
  const S = STYLES[style] || STYLES.site;
  const hide = new Set(opts.hide || []);
  const only = opts.only || null;   // legend hover: only this layer
  const on = (layer) => (only ? layer === only : !hide.has(layer));
  const b = bounds(plan.outline);
  const span = Math.max(b.x1 - b.x0, b.y1 - b.y0);
  const m = Math.max(14, span * 0.08);
  const vb = [b.x0 - m, -(b.y1 + m), b.x1 - b.x0 + 2 * m, b.y1 - b.y0 + 2 * m];
  const W = opts.w || 900, H = Math.round(W * vb[3] / vb[2]);
  const k = Math.max(1, span / 160);   // symbol scale: markers stay readable on big sites
  const o = [];
  o.push(`<svg xmlns="http://www.w3.org/2000/svg" viewBox="${vb.map(f).join(' ')}" width="${W}" height="${H}">`);
  o.push(`<defs><filter id="soft" x="-30%" y="-30%" width="160%" height="160%"><feGaussianBlur stdDeviation="${f(0.7)}"/></filter>
    <filter id="grain" x="0" y="0" width="100%" height="100%"><feTurbulence type="fractalNoise" baseFrequency="${f(0.08 / k)}" numOctaves="3" seed="4"/>
      <feColorMatrix values="0 0 0 0 0.25  0 0 0 0 0.32  0 0 0 0 0.16  0 0 0 0.35 0"/><feComposite in2="SourceGraphic" operator="in"/></filter>
    <clipPath id="site"><polygon points="${poly(plan.outline)}"/></clipPath></defs>`);
  // the world: grass with a little grain, the city's own streets in the game's light grey with kerbs
  o.push(`<rect x="${f(vb[0])}" y="${f(vb[1])}" width="${f(vb[2])}" height="${f(vb[3])}" fill="${S.ground}"/>`);
  o.push(`<rect x="${f(vb[0])}" y="${f(vb[1])}" width="${f(vb[2])}" height="${f(vb[3])}" fill="#fff" filter="url(#grain)"/>`);
  for (const r of plan.roads || []) {
    const d = pathD([r.pts[0], r.pts[3]]);
    o.push(`<path d="${d}" stroke="${S.kerb}" stroke-width="${f(r.w + 0.8)}" fill="none"/><path d="${d}" stroke="${S.street}" stroke-width="${f(r.w - 3)}" fill="none"/>`);
  }

  // 1. the site: one quiet tint (B: dimmed, so the linework pops on any ground)
  o.push(`<polygon points="${poly(plan.outline)}" fill="${S.siteTint[0]}" opacity="${S.siteTint[1]}"/>`);

  // 2. surfaces: fills of the merged result, no outlines; group opacity so overlapping pieces never double up
  const surf = { Asphalt: [S.asphalt, S.asphaltA], Grass: [S.grass, S.grassA], Paving: [S.paving, S.pavingA], Plaza: [S.plaza, S.plazaA], Water: [S.water, S.waterA], Decorative: [S.deco, S.decoA], Ground: [S.groundSurf, S.groundSurfA] };
  o.push('<g clip-path="url(#site)">');
  for (const role of ['Ground', 'Grass', 'Decorative', 'Paving', 'Plaza', 'Asphalt', 'Water']) {
    if (!on(role === 'Water' ? 'water' : 'surfaces')) continue;
    const rings = plan.surfaces.filter((s) => s.role === role);
    if (!rings.length) continue;
    const [c, a] = surf[role];
    o.push(`<g opacity="${a}">` + rings.map((s) => `<polygon points="${poly(s.ring)}" fill="${c}" stroke="${c}" stroke-width="0.3"/>`).join('') + '</g>');
  }
  // zoning: shop plots in the game's zone colour with its cell grid (what the zoning tool shows)
  if (on('cutouts')) {
    const cells = plan.cells.filter((c) => c.state === 'Zoned');
    const quads = cells.length ? cells.map((c) => [c.quad, c.zone]) : plan.zoning.map((z) => [z.quad, z.zone]);
    if (quads.length) {
      o.push(`<g opacity="${S.line ? 0.35 : 0.5}">` + quads.map(([q, z]) => `<polygon points="${poly(q)}" fill="${zoneColor(S, z)}"/>`).join('') + '</g>');
      o.push(`<g fill="none" stroke="#fff" stroke-opacity="0.5" stroke-width="0.25">` + quads.map(([q]) => `<polygon points="${poly(q)}"/>`).join('') + '</g>');
    }
  }
  o.push('</g>');

  // 3. nets at their real width, round joins so a junction is one surface
  const isCar = (n) => n.role !== 'Fence' && (n.layer === 'parking' || (n.layer.startsWith('road:') && n.layer !== 'road:carFree'));
  const isWalk = (n) => n.layer === 'paths' || n.layer === 'road:carFree';
  const cars = plan.nets.filter((n) => isCar(n) && on(n.layer));
  const caps = on('road:street') || on('road:local') ? plan.objects.filter((x) => x.role === 'CulDeSacCap' || x.role === 'RoundaboutIsland') : [];
  const capR = 10;
  const carBody = cars.map((n) => `<path d="${pathD(n.pts)}" stroke-width="${f(n.hw * 2)}"/>`).join('') + caps.map((c) => `<circle cx="${f(c.pos[0])}" cy="${f(-c.pos[1])}" r="${capR}" stroke-width="0"/>`).join('');
  if (S.line) {
    // B: the kerbs as two thin white lines = a fat white stroke with the inside knocked out
    o.push(`<g stroke="#fff" fill="#fff" stroke-opacity="0.9" fill-opacity="0.9" stroke-linecap="round" stroke-linejoin="round">` + cars.map((n) => `<path d="${pathD(n.pts)}" fill="none" stroke-width="${f(n.hw * 2 + 0.6)}"/>`).join('') + caps.map((c) => `<circle cx="${f(c.pos[0])}" cy="${f(-c.pos[1])}" r="${capR + 0.3}"/>`).join('') + '</g>');
    o.push(`<g stroke="#26313d" fill="#26313d" stroke-linecap="round" stroke-linejoin="round">` + cars.map((n) => `<path d="${pathD(n.pts)}" fill="none" stroke-width="${f(n.hw * 2 - 0.6)}"/>`).join('') + caps.map((c) => `<circle cx="${f(c.pos[0])}" cy="${f(-c.pos[1])}" r="${capR - 0.3}"/>`).join('') + '</g>');
  } else {
    o.push(`<g opacity="${S.roadA}" stroke="${S.road}" fill="${S.road}" stroke-linecap="round" stroke-linejoin="round">` + carBody.replace(/<path /g, '<path fill="none" ') + '</g>');
  }
  o.push(`<g stroke="${S.roadMark}" stroke-opacity="${S.line ? 0.5 : 0.7}" fill="none" stroke-width="0.3" stroke-dasharray="3 3">` +
    cars.filter((n) => n.layer.startsWith('road:') && n.hw >= 3).map((n) => `<path d="${pathD(n.pts)}"/>`).join('') + '</g>');
  o.push(`<g opacity="${S.pathA}" stroke="${S.path}" fill="none" stroke-linecap="round" stroke-linejoin="round">` +
    plan.nets.filter((n) => isWalk(n) && on(n.layer)).map((n) => `<path d="${pathD(n.pts)}" stroke-width="${f(Math.max(1.2, n.hw * 2))}"/>`).join('') + '</g>');
  if (on('objects')) o.push(plan.nets.filter((n) => n.role === 'Fence').map((n) => `<path d="${pathD(n.pts)}" stroke="${S.line ? '#fff' : '#6b5440'}" stroke-width="0.35" fill="none"/>`).join(''));

  // 4. parking stalls as the painted lines
  if (on('parking')) {
    const aisles = plan.nets.filter((n) => n.layer === 'parking').map((n) => n.pts);
    let d = '';
    for (const bay of plan.objects.filter((x) => x.role === 'ParkingBay')) for (const s of bayLines(bay.poly, aisles)) d += `M${pt(s[0])} L${pt(s[1])}`;
    o.push(`<path d="${d}" stroke="${S.bay}" stroke-opacity="${S.line ? 0.75 : 0.9}" stroke-width="0.18" fill="none"/>`);
    for (const c of plan.objects.filter((x) => x.role === 'Charger')) o.push(`<circle cx="${f(c.pos[0])}" cy="${f(-c.pos[1])}" r="0.7" fill="#3fd28a"/>`);
  }

  // 5. places (playground, picnic, kiosk, restroom…): one warm colour at the real footprint
  if (on('features')) for (const x of plan.objects.filter((x) => x.layer === 'features'))
    o.push(`<polygon points="${poly(x.poly)}" fill="${S.feature}" fill-opacity="${S.line ? 0.25 : 1}" stroke="${S.featureEdge}" stroke-width="${S.line ? 0.35 : 0.25}"/>`);

  // 6. small furniture: small and neutral
  if (on('objects')) for (const x of plan.objects.filter((x) => x.layer === 'objects')) {
    const [cx, cz] = x.pos;
    if (x.role === 'Lamp') o.push(`<circle cx="${f(cx)}" cy="${f(-cz)}" r="0.45" fill="${S.lamp}" stroke="#3a3a3a" stroke-width="0.12"/>`);
    else if (x.role === 'Bench') o.push(`<polygon points="${poly(x.poly)}" fill="${S.bench}" opacity="${S.line ? 0.7 : 1}"/>`);
    else if (x.role !== 'Fence') o.push(`<circle cx="${f(cx)}" cy="${f(-cz)}" r="0.35" fill="${S.furniture}" opacity="${S.line ? 0.6 : 1}"/>`);
  }

  // 7. planting last: crowns over everything; bushes close to the lawn colour (no confetti)
  if (on('planting')) o.push(`<g fill="${S.bush}" opacity="${S.bushA}">` + plan.objects.filter((x) => x.layer === 'planting').map((x) => `<circle cx="${f(x.pos[0])}" cy="${f(-x.pos[1])}" r="${f(Math.max(0.5, Math.min(x.r, 1.6)))}"/>`).join('') + '</g>');
  if (on('trees')) {
    const trees = plan.objects.filter((x) => x.layer === 'trees');
    if (S.line) {
      o.push(`<g fill="${S.tree}" fill-opacity="0.12" stroke="${S.tree}" stroke-width="0.35">` + trees.map((x) => `<circle cx="${f(x.pos[0])}" cy="${f(-x.pos[1])}" r="${f(x.r)}"/>`).join('') + '</g>');
      o.push(`<g fill="${S.tree}">` + trees.map((x) => `<circle cx="${f(x.pos[0])}" cy="${f(-x.pos[1])}" r="0.4"/>`).join('') + '</g>');
    } else {
      o.push(`<g fill="${S.shadow}" filter="url(#soft)">` + trees.map((x) => `<circle cx="${f(x.pos[0] + x.r * 0.25)}" cy="${f(-x.pos[1] + x.r * 0.3)}" r="${f(x.r)}"/>`).join('') + '</g>');
      o.push(`<g fill="${S.tree}">` + trees.map((x) => `<circle cx="${f(x.pos[0])}" cy="${f(-x.pos[1])}" r="${f(x.r)}"/>`).join('') + '</g>');
      o.push(`<g fill="${S.treeHi}">` + trees.map((x) => `<circle cx="${f(x.pos[0] - x.r * 0.22)}" cy="${f(-x.pos[1] - x.r * 0.22)}" r="${f(x.r * 0.55)}"/>`).join('') + '</g>');
    }
  }

  // 8. the outline: one clean line with a dark halo for contrast on any ground
  o.push(`<polygon points="${poly(plan.outline)}" fill="none" stroke="#0b1a2a" stroke-opacity="0.35" stroke-width="${f(1.6 * k)}" stroke-linejoin="round"/>`);
  o.push(`<polygon points="${poly(plan.outline)}" fill="none" stroke="${S.outline}" stroke-width="${f(0.7 * k)}" stroke-linejoin="round"/>`);

  // 9. entrances: a white badge, the arrow pointing in
  if (on('entrances')) {
    const c = centroid(plan.outline);
    for (const e of plan.markers) {
      const [x, z] = e.pos; const ang = Math.atan2(-(c[1] - z), c[0] - x) * 180 / Math.PI;
      o.push(`<g transform="translate(${f(x)},${f(-z)}) scale(${f(k)}) rotate(${f(ang)})"><circle r="3.2" fill="#ffffff" stroke="#0b1a2a" stroke-opacity="0.4" stroke-width="0.4"/>
        <path d="M-1.2,-1.6 L1.4,0 L-1.2,1.6" fill="none" stroke="${S.accent}" stroke-width="0.85" stroke-linecap="round" stroke-linejoin="round"/></g>`);
    }
  }
  // 10. only real problems: a red badge with "!"
  if (on('issues')) for (const i of plan.issues.filter((i) => i.red))
    o.push(`<g transform="translate(${f(i.pos[0])},${f(-i.pos[1])}) scale(${f(k)})"><circle r="3.2" fill="#e5322d" stroke="#fff" stroke-width="0.5"/><text y="1.6" text-anchor="middle" font-size="4.6" font-weight="700" fill="#fff" font-family="Segoe UI,Arial">!</text></g>`);

  if (opts.scale !== false) o.push(scaleBar(vb));
  o.push('</svg>');
  return o.join('\n');
}

function centroid(r) { let x = 0, z = 0; for (const p of r) { x += p[0]; z += p[1]; } return [x / r.length, z / r.length]; }

function scaleBar(vb) {
  const len = vb[2] > 600 ? 100 : vb[2] > 250 ? 50 : 20;
  const t = vb[2] / 300, x = vb[0] + vb[2] * 0.04, y = vb[1] + vb[3] * 0.955;
  return `<g font-family="Segoe UI,Arial" font-size="${f(t * 8)}" fill="#fff"><rect x="${f(x)}" y="${f(y - t * 1.6)}" width="${len}" height="${f(t * 1.6)}" fill="#fff" opacity="0.95"/><text x="${f(x + len + t * 3)}" y="${f(y + t * 0.6)}">${len} m</text></g>`;
}

// Legend rows: what is drawn, in plain words, each with a sample of the real symbol.
function legendRows(plan) {
  const rows = [];
  const len = (p) => { let s = 0; for (let i = 1; i < p.length; i++) s += d2(p[i], p[i - 1]); return s; };
  const roadLen = plan.nets.filter((n) => n.layer.startsWith('road:') && n.layer !== 'road:carFree').reduce((s, n) => s + len(n.pts), 0);
  const walkLen = plan.nets.filter((n) => n.layer === 'paths' || n.layer === 'road:carFree').reduce((s, n) => s + len(n.pts), 0);
  const count = (fn) => plan.objects.filter(fn).length;
  const bays = count((x) => x.role === 'ParkingBay'), trees = count((x) => x.layer === 'trees'), plants = count((x) => x.layer === 'planting');
  const places = count((x) => x.layer === 'features'), furn = count((x) => x.layer === 'objects');
  const plots = plan.cells.filter((c) => c.state === 'Zoned').length;
  const area = (r) => { let a = 0; for (let i = 0; i < r.length; i++) { const p = r[i], q = r[(i + 1) % r.length]; a += p[0] * q[1] - q[0] * p[1]; } return Math.abs(a / 2); };
  const asphalt = plan.surfaces.filter((s) => s.role === 'Asphalt').reduce((s, x) => s + area(x.ring), 0);
  if (roadLen) rows.push({ id: 'roads', sym: 'road', label: 'Roads', n: `${Math.round(roadLen).toLocaleString('en')} m` });
  if (bays) rows.push({ id: 'parking', sym: 'bays', label: 'Parking spaces', n: `${bays}` });
  else if (asphalt) rows.push({ id: 'surfaces', sym: 'bays', label: 'Car park', n: '' });
  if (walkLen) rows.push({ id: 'paths', sym: 'path', label: 'Footpaths', n: `${Math.round(walkLen)} m` });
  if (plan.surfaces.some((s) => s.role === 'Grass')) rows.push({ id: 'surfaces', sym: 'grass', label: 'Lawn', n: '' });
  if (plan.surfaces.some((s) => s.role === 'Paving' || s.role === 'Plaza' || s.role === 'Decorative')) rows.push({ id: 'surfaces', sym: 'paving', label: 'Paving', n: '' });
  if (plan.surfaces.some((s) => s.role === 'Water')) rows.push({ id: 'water', sym: 'water', label: 'Water', n: '' });
  if (plots) rows.push({ id: 'cutouts', sym: 'zone', label: 'Shops grow here', n: `${plots} cells` });
  if (trees) rows.push({ id: 'trees', sym: 'tree', label: 'Trees', n: `${trees}` });
  if (plants) rows.push({ id: 'planting', sym: 'bush', label: 'Bushes and flower beds', n: `${plants}` });
  if (places) rows.push({ id: 'features', sym: 'feature', label: 'Playground, picnic, kiosk', n: `${places}` });
  if (furn) rows.push({ id: 'objects', sym: 'furniture', label: 'Benches, lamps, bins', n: `${furn}` });
  if (plan.markers.length) rows.push({ id: 'entrances', sym: 'entrance', label: 'Entrances', n: `${plan.markers.length}` });
  const red = plan.issues.filter((i) => i.red).length;
  if (red) rows.push({ id: 'issues', sym: 'issue', label: 'Needs a look', n: `${red}` });
  return rows;
}

// Legend swatches: the same colours and shapes as the map, at legend size.
function swatch(sym, style = 'site') {
  const S = STYLES[style] || STYLES.site;
  const bg = S.line ? '#2a3542' : S.ground;
  const box = (inner) => `<svg viewBox="0 0 30 18" width="30" height="18" xmlns="http://www.w3.org/2000/svg"><rect width="30" height="18" rx="3" fill="${bg}"/>${inner}</svg>`;
  const road = S.line ? `<rect x="-1" y="5" width="32" height="8" fill="#26313d" stroke="#fff" stroke-width="0.8"/>` : `<rect x="0" y="5" width="30" height="8" fill="${S.road}" opacity="${S.roadA}"/>`;
  switch (sym) {
    case 'road': return box(`${road}<line x1="1" y1="9" x2="29" y2="9" stroke="${S.roadMark}" stroke-width="0.8" stroke-dasharray="3 3"/>`);
    case 'bays': return box(`<rect width="30" height="18" rx="3" fill="${S.asphalt}" opacity="${S.line ? 0.15 : S.asphaltA}"/>${[5, 11, 17, 23].map((x) => `<line x1="${x}" y1="3" x2="${x}" y2="11" stroke="#fff" stroke-width="0.9"/>`).join('')}<line x1="5" y1="3" x2="23" y2="3" stroke="#fff" stroke-width="0.9"/>`);
    case 'path': return box(`<path d="M3,13 C11,3 19,15 27,5" stroke="${S.path}" stroke-width="3.4" fill="none" stroke-linecap="round" opacity="${S.pathA}"/>`);
    case 'grass': return box(`<rect x="3" y="3" width="24" height="12" rx="2" fill="${S.grass}" opacity="${Math.min(1, S.grassA * 2)}"/>`);
    case 'paving': return box(`<rect x="3" y="3" width="24" height="12" rx="2" fill="${S.paving}" opacity="${S.line ? 0.4 : S.pavingA}"/>`);
    case 'water': return box(`<rect x="3" y="3" width="24" height="12" rx="5" fill="${S.water}" opacity="${S.waterA}"/>`);
    case 'tree': return S.line ? box(`<circle cx="15" cy="9" r="6" fill="${S.tree}" fill-opacity="0.15" stroke="${S.tree}" stroke-width="0.9"/><circle cx="15" cy="9" r="0.9" fill="${S.tree}"/>`)
      : box(`<circle cx="16" cy="10" r="6" fill="${S.shadow}"/><circle cx="15" cy="9" r="6" fill="${S.tree}"/><circle cx="13.6" cy="7.6" r="3.3" fill="${S.treeHi}"/>`);
    case 'bush': return box(`<g fill="${S.bush}" opacity="${S.bushA}"><circle cx="9" cy="9" r="2.3"/><circle cx="15" cy="10" r="1.9"/><circle cx="21" cy="8" r="2.1"/></g>`);
    case 'feature': return box(`<rect x="9" y="4" width="12" height="10" fill="${S.feature}" fill-opacity="${S.line ? 0.25 : 1}" stroke="${S.featureEdge}" stroke-width="0.9"/>`);
    case 'zone': return box(`<g opacity="0.8">${[0, 1, 2].map((i) => `<rect x="${5 + i * 7}" y="4" width="6.4" height="10" fill="${S.zone.commercialLow}"/>`).join('')}</g>`);
    case 'furniture': return box(`<circle cx="8" cy="9" r="1.5" fill="${S.lamp}" stroke="#333" stroke-width="0.4"/><rect x="12.5" y="8" width="5" height="2" fill="${S.bench}"/><circle cx="22" cy="9" r="1.3" fill="${S.furniture}"/>`);
    case 'entrance': return box(`<circle cx="15" cy="9" r="6.2" fill="#fff"/><path d="M12.8,6 L17.4,9 L12.8,12" fill="none" stroke="${S.accent}" stroke-width="1.7" stroke-linecap="round" stroke-linejoin="round"/>`);
    case 'issue': return box(`<circle cx="15" cy="9" r="6.2" fill="#e5322d"/><text x="15" y="12.6" text-anchor="middle" font-size="9.5" font-weight="700" fill="#fff" font-family="Segoe UI,Arial">!</text>`);
  }
  return box('');
}

if (typeof module !== "undefined") module.exports = { render, legendRows, swatch, STYLES };

if (typeof require !== "undefined" && require.main === module) {
  const fs = require("fs");
  const [file, style = 'site', ...rest] = process.argv.slice(2);
  const opts = {};
  for (let i = 0; i < rest.length; i++) {
    if (rest[i] === '--w') opts.w = +rest[++i];
    else if (rest[i] === '--hide') opts.hide = rest[++i].split(',');
    else if (rest[i] === '--only') opts.only = rest[++i];
  }
  process.stdout.write(render(JSON.parse(fs.readFileSync(file, 'utf8')), style, opts));
}
