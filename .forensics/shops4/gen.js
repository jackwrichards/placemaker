// node gen.js <out.json> [styleFilter] [landFilter]: the kept shops styles on small, odd, sharp shapes with odd entrances.
const fs = require('fs');
const [out, sf, lf, es] = process.argv.slice(2);
const styles = [['stripMall','pondRow'],['stripMall','plazaRow'],['stripMall','parkRow'],['stripMall','classic'],['stripMall','bigBox'],['mallDistrict','district'],['mallDistrict','dense'],
  ['mallPark','commons'],['mallPark','garden'],['mallPark','family'],['marketSquare','bigMarket']];
const lands = {
  block: null,
  tiny: { shape: 'rect', size: [40, 30], gap: 10 },
  small: { shape: 'rect', size: [72, 55], gap: 10 },
  thin: { shape: 'rect', size: [170, 32], gap: 10 },
  tri: { shape: 'points', points: [[0,0],[130,0],[15,95]], roads: 'follow', gap: 10 },
  l: { shape: 'l', size: [200, 180], roads: 'follow', gap: 10 },
  circle: { shape: 'circle', size: [140, 140], roads: 'follow', gap: 10 },
  big: { shape: 'rect', size: [300, 280], gap: 10 },
  mid: { shape: 'rect', size: [220, 220], gap: 10 },
  long: { shape: 'rect', size: [400, 160], gap: 10 },
  slope: { shape: 'rect', size: [150, 130], gap: 10, ground: 'slope', grade: 0.06, toward: 'north' },
  odd: { shape: 'points', points: [[0,0],[100,-10],[140,50],[60,60],[70,110],[-10,80]], roads: 'follow', gap: 10 },
};
const car = t => t === 'marketSquare' ? 'access' : 'entrance';
const sets = {
  oneway: [{ side: 'south', t: 0.03, kind: 'entry' }, { side: 'east', t: 0.08, kind: 'exit' }, { side: 'north', t: 0.5, kind: 'alley' }, { side: 'west', t: 0.9, kind: 'footpath' }, { side: 'east', t: 0.6, kind: 'footpath' }],
  corners: [{ side: 'south', t: 0.97, kind: 'entrance' }, { side: 'west', t: 0.04, kind: 'entrance' }, { side: 'north', t: 0.95, kind: 'footpath' }],
  back: [{ side: 'north', t: 0.5, kind: 'entrance' }, { side: 'south', t: 0.5, kind: 'footpath' }],
};
const ents = (t, land) => es && sets[es] ? sets[es] : land === 'tiny'
  ? [{ side: 'south', t: 0.12, kind: car(t) }]
  : [{ side: 'south', t: 0.06, kind: car(t) }, { side: 'east', t: 0.92, kind: car(t) }, { side: 'west', t: 0.5, kind: 'footpath' }];
const scenarios = [];
for (const [t, p] of styles) for (const [ln, land] of Object.entries(lands)) {
  if (sf && !new RegExp(sf).test(`${t}-${p}`)) continue;
  if (lf && !new RegExp(lf).test(ln)) continue;
  const steps = [{ do: 'area', tool: land ? 'land' : 'bucket' }, { do: 'type', layer: t, preset: p }];
  for (const e of ents(t, ln)) steps.push({ do: 'entrance', ...e });
  steps.push({ do: 'preview', shot: 'plan', shotMode: /tiny|thin/.test(ln) ? 'ui' : 'overlay', camera: { target: 'build', view: 'top' } });
  steps.push({ do: 'build' }, { do: 'grow', seconds: 20 });
  steps.push({ do: 'camera', target: 'build', view: 'oblique', pitch: 45, shot: '45-clean', shotMode: 'render' });
  steps.push({ do: 'camera', target: 'build', view: 'top', shot: 'top-zoning', shotMode: 'render', zones: true });
  steps.push({ do: 'camera', target: 'build', view: 'closeup', shot: 'close-clean', shotMode: 'render' });
  steps.push({ do: 'terrain' });
  const s = { id: `${t}-${p}-${ln}${es ? "-" + es : ""}`, pace: 'settled', steps };
  if (land) s.land = land; else s.site = 'flat';
  scenarios.push(s);
}
fs.writeFileSync(out, JSON.stringify({ scenarios }, null, 1));
console.log(scenarios.length, 'scenarios');
