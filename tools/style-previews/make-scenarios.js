// tools/style-previews/make-scenarios.js — game-runner scenario files that photograph every visible style (the user, 25 Oct:
// "take a photo of all of the places and all the subtypes ... the same angle for all of them").
//
// Per category one file suite/scenarios/previews-<category>.json: one build per style, each on its own 150 x 140 m land with a
// ring road (roads: open 420 x 320 m land); the zoning styles grown in place (debug fast spawn, the simulation running); three
// pictures from fixed cameras: close (a low cinematic look into its middle), iso (an isometric-looking view: 45 degrees round,
// 35 down, a narrow lens) and top (straight down). Same
// angles for every style. No neighbourhood around it (the user: zoned plots that never grew looked wrong).
// Then tools/style-previews/collect.ps1 shrinks the pictures into UI/src/assets/previews, which the panel bundles.
//
//   node tools/style-previews/make-scenarios.js [category ...]      (default: every visible category)
//   powershell -File tools/suite/Send-Suite.ps1 -File suite/scenarios/previews-parks.json -Build -Who style-previews

const fs = require('fs'), path = require('path');
const repo = path.join(__dirname, '..', '..');
const cat = JSON.parse(fs.readFileSync(path.join(repo, 'suite', 'catalog.json'), 'utf8'));
const shelfDir = path.join(repo, 'Recipes', 'shelf');
const shelves = fs.readdirSync(shelfDir).map((f) => JSON.parse(fs.readFileSync(path.join(shelfDir, f), 'utf8'))).filter((s) => !s.hidden);

/** Every visible type and style of a category, as the Make row shows them. */
function styles(shelf) {
  const hiddenTypes = new Set(shelf.hiddenTypes || []);
  const out = [];
  for (const t of cat.types) {
    if (!((shelf.groups || []).includes(t.group) || (shelf.types || []).includes(t.layer)) || hiddenTypes.has(t.layer)) continue;
    const hidden = new Set((shelf.hiddenStyles || {})[t.layer] || []);
    for (const p of t.presets) if (!hidden.has(p)) out.push({ layer: t.layer, preset: p, group: t.group });
  }
  return out;
}

const LAND = { shape: 'rect', size: [150, 140], gap: 10, roads: 'around' };
const OPEN = { shape: 'rect', size: [420, 320], roads: 'none' };

// (the user: "a cooler cinematic, really close up, and then an isometric looking view, and then a top down")
const CAMERAS = [
  { name: 'close', camera: { do: 'camera', target: 'build', view: 'closeup', pitch: 26, distance: 62, yaw: 30 } },
  { name: 'iso', camera: { do: 'camera', target: 'build', view: 'oblique', yaw: 45, pitch: 35, fov: 22 } },
  { name: 'top', camera: { do: 'camera', target: 'build', view: 'top' } },
];

const idOf = (s) => ('prv-' + s.layer + '-' + s.preset).replace(/[^A-Za-z0-9_-]/g, '_');

function file(shelf, done) {
  const scenarios = [];
  for (const s of styles(shelf)) {
    const id = idOf(s);
    if (done.has(id)) continue;   // a type shown in two categories (the amphitheatre) is photographed once
    done.add(id);
    const roads = s.layer === 'roads', zoning = s.group === 'zoning';
    const steps = [{ do: 'area', tool: 'land' }, { do: 'type', layer: s.layer, preset: s.preset }];
    if (!roads && !zoning) steps.push({ do: 'entrances' });
    steps.push({ do: 'build' });
    if (zoning) steps.push({ do: 'grow', seconds: 60 });
    for (const c of CAMERAS) steps.push({ ...c.camera, shot: c.name, shotMode: 'render' });
    scenarios.push({ id, pace: 'settled', land: roads ? OPEN : LAND, steps });
  }
  if (!scenarios.length) return null;
  return {
    _about: 'Style previews, ' + shelf.name + ': every visible style built on its own land, three pictures each (close, iso, top); ' +
      'the zoning styles grown first. Made by tools/style-previews/make-scenarios.js; collect with tools/style-previews/collect.ps1. ' +
      'Send with: powershell -File tools/suite/Send-Suite.ps1 -File suite/scenarios/previews-' + shelf.id + '.json -Build -Who style-previews',
    scenarios,
  };
}

const want = process.argv.slice(2);
const done = new Set();
for (const shelf of shelves.sort((a, b) => a.order - b.order)) {
  if (want.length && !want.includes(shelf.id)) { styles(shelf).forEach((s) => done.has(idOf(s))); continue; }
  const f = file(shelf, done);
  if (!f) continue;
  const out = path.join(repo, 'suite', 'scenarios', 'previews-' + shelf.id + '.json');
  fs.writeFileSync(out, JSON.stringify(f, null, 2));
  console.log(out + ': ' + f.scenarios.length + ' styles');
}
