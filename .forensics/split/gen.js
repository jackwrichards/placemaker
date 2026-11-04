// node gen.js <out.json>: the strip mall's shops-to-parking split on the user's block (two car entrances near the ends of one
// street, a footpath between) and on other sizes.
const fs = require('fs');
const [out] = process.argv.slice(2);
const user = [{ side: 'east', t: 0.06, kind: 'entrance' }, { side: 'east', t: 0.94, kind: 'entrance' }, { side: 'east', t: 0.5, kind: 'footpath' }];
const mid = [{ side: 'south', t: 0.5, kind: 'entrance' }, { side: 'south', t: 0.4, kind: 'footpath' }];
const builds = [
  ['plazaRow', 'user', { shape: 'rect', size: [190, 180], gap: 10 }, user],
  ['classic', 'user', { shape: 'rect', size: [190, 180], gap: 10 }, user],
  ['classic', 'block', null, mid],
  ['classic', 'mid', { shape: 'rect', size: [220, 220], gap: 10 }, mid],
  ['plazaRow', 'big', { shape: 'rect', size: [300, 280], gap: 10 }, mid],
  ['classic', 'long', { shape: 'rect', size: [400, 160], gap: 10 }, mid],
];
const scenarios = builds.map(([p, ln, land, ents]) => {
  const steps = [{ do: 'area', tool: land ? 'land' : 'bucket' }, { do: 'type', layer: 'stripMall', preset: p }];
  for (const e of ents) steps.push({ do: 'entrance', ...e });
  steps.push({ do: 'preview', shot: 'plan', shotMode: 'overlay', camera: { target: 'build', view: 'top' } });
  steps.push({ do: 'build' }, { do: 'grow', seconds: 20 });
  steps.push({ do: 'camera', target: 'build', view: 'oblique', pitch: 45, shot: '45-clean', shotMode: 'render' });
  steps.push({ do: 'camera', target: 'build', view: 'top', shot: 'top-zoning', shotMode: 'render', zones: true });
  steps.push({ do: 'terrain' });
  const s = { id: `stripMall-${p}-${ln}`, pace: 'settled', steps };
  if (land) s.land = land; else s.site = 'flat';
  return s;
});
fs.writeFileSync(out, JSON.stringify({ scenarios }, null, 1));
console.log(scenarios.length, 'scenarios');
