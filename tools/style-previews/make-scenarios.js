// tools/style-previews/make-scenarios.js — game-runner scenario files that photograph every visible style (the user, 25 Oct:
// "take a photo of all of the places and all the subtypes ... in a real city ... the same angle for all of them").
//
// Per category one file suite/scenarios/previews-<category>.json:
//   1. one build per style, each on its own 150 x 140 m land with a ring road (roads: open 420 x 320 m land), the homes of a
//      neighbourhood zoned along the outside of the ring road (lab zoneRect, three cells deep);
//   2. the city grown once (the homes, and the zoning types' own plots);
//   3. three pictures of every build from fixed cameras: close (a close look at its middle), 45 (45 degrees from the south) and
//      top (straight down). Same angles for every style.
// Then tools/style-previews/collect.js shrinks the pictures into UI/src/assets/previews and writes the list the panel reads.
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

// the land: 150 x 140 m inside a 10 m gap and a small ring road (centre line at ±85 / ±80 from the land's middle)
const LAND = { shape: 'rect', size: [150, 140], gap: 10, roads: 'around' };
const OPEN = { shape: 'rect', size: [420, 320], roads: 'none' };
const ROAD_X = 85, ROAD_Z = 80, ROAD_HALF = 6.5, DEPTH = 24, REACH = 120;
const zoneAround = [
  [-REACH, -ROAD_Z - ROAD_HALF - DEPTH, REACH, -ROAD_Z - ROAD_HALF],
  [-REACH, ROAD_Z + ROAD_HALF, REACH, ROAD_Z + ROAD_HALF + DEPTH],
  [-ROAD_X - ROAD_HALF - DEPTH, -ROAD_Z + 8, -ROAD_X - ROAD_HALF, ROAD_Z - 8],
  [ROAD_X + ROAD_HALF, -ROAD_Z + 8, ROAD_X + ROAD_HALF + DEPTH, ROAD_Z - 8],
].map((rect, i) => ({ do: 'lab', op: 'zoneRect', rect, zone: i % 2 ? 'EU Residential Low' : 'EU Residential Low' }));

const CAMERAS = [
  { name: 'close', camera: { do: 'camera', target: 'build', view: 'closeup' } },
  { name: '45', camera: { do: 'camera', target: 'build', view: 'oblique', pitch: 45 } },
  { name: 'top', camera: { do: 'camera', target: 'build', view: 'top' } },
];

const idOf = (s) => ('prv-' + s.layer + '-' + s.preset).replace(/[^A-Za-z0-9_-]/g, '_');

function file(shelf) {
  const list = styles(shelf);
  const scenarios = [];
  for (const s of list) {
    const roads = s.layer === 'roads', zoning = s.group === 'zoning';
    const steps = [{ do: 'area', tool: 'land' }, { do: 'type', layer: s.layer, preset: s.preset }];
    if (!roads && !zoning) steps.push({ do: 'entrances' });
    steps.push({ do: 'build' });
    if (!roads) steps.push(...zoneAround);
    scenarios.push({ id: idOf(s), pace: 'settled', land: roads ? OPEN : LAND, steps });
  }
  if (!scenarios.length) return null;
  scenarios.push({ id: 'prv-grow-' + shelf.id, on: scenarios[scenarios.length - 1].id, steps: [{ do: 'grow', seconds: 90 }] });
  for (const s of list)
    scenarios.push({
      id: idOf(s) + '-shots', on: idOf(s),
      steps: CAMERAS.map((c) => ({ ...c.camera, shot: c.name, shotMode: 'render' })),
    });
  return {
    _about: `Style previews, ${shelf.name}: every visible style built in a small neighbourhood, the city grown, three pictures each (close, 45, top). Made by tools/style-previews/make-scenarios.js; collect with tools/style-previews/collect.js. Send with: powershell -File tools/suite/Send-Suite.ps1 -File suite/scenarios/previews-${shelf.id}.json -Build -Who style-previews`,
    scenarios,
  };
}

const want = process.argv.slice(2);
for (const shelf of shelves.sort((a, b) => a.order - b.order)) {
  if (want.length && !want.includes(shelf.id)) continue;
  const f = file(shelf);
  if (!f) continue;
  const out = path.join(repo, 'suite', 'scenarios', 'previews-' + shelf.id + '.json');
  fs.writeFileSync(out, JSON.stringify(f, null, 2));
  console.log(out + ': ' + (f.scenarios.length - 1) / 2 + ' styles');
}
