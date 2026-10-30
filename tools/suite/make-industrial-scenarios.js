// Writes suite/scenarios/ground-industrial-all.json: every surviving industrial style built on 220 x 200, its library
// instances waited for, then the whole yard from 45 degrees, a middle view and close looks at the marks of its work
// (docs/runbooks/industrial.md). Send with:
//   powershell -File tools/suite/Send-Suite.ps1 -File suite/scenarios/ground-industrial-all.json -Who makeover-industrial
// Optional: node tools/suite/make-industrial-scenarios.js <size> (e.g. 150x140) <ground> (flat | slope) <type-style,...>
const fs = require('fs'), path = require('path');
const size = (process.argv[2] || '220x200').split('x').map(Number);
const ground = process.argv[3] || 'flat';
const styles = [
  ['containerYard', 'terminal', ['rustRunoff', 'laneGrime', 'fadedLineYellow', 'oilSpot']],
  ['containerYard', 'depot', ['rustRunoff', 'crackConcrete', 'laneGrime', 'puddlePiece']],
  ['containerYard', 'storage', ['crackWeeds', 'fadedLineYellow', 'weedStrip', 'skidMark']],
  ['carYard', 'salvage', ['yardRut', 'oilSpot', 'rustRunoff', 'weedStrip']],
  ['carYard', 'crusher', ['scrapGrit', 'yardRut', 'dustDrift', 'puddlePiece']],
  ['carYard', 'breakers', ['oilSpot', 'yardRut', 'rustRunoff', 'oilDrips']],
  ['junkYard', 'sorted', ['scrapGrit', 'dustDrift', 'litterMix', 'yardRut']],
  ['industrialWorks', 'salvage', ['rustRunoff', 'laneGrime', 'crackWeeds', 'oilSpot']],
  ['industrialWorks', 'works', ['dustDrift', 'crackConcrete', 'oilSpot', 'pebbles']],
  ['landfill', 'active', ['litterMix', 'leachateSeep', 'yardRut', 'weedStrip']],
];
const land = { size, gap: 10 };
if (ground === 'slope') { land.ground = 'slope'; land.grade = 0.05; }
const only = (process.argv[4] || '').split(',').filter(Boolean);
const scenarios = styles.filter(([l, pr]) => only.length === 0 || only.includes(l + '-' + pr)).map(([layer, preset, kinds]) => {
  const steps = [
    { do: 'area', tool: 'land' },
    { do: 'type', layer, preset },
    { do: 'entrances' },
    { do: 'build' },
    { do: 'lab', op: 'grime' },
    { do: 'camera', target: 'build', view: 'oblique', pitch: 45, shot: '45', shotMode: 'render' },
    { do: 'camera', target: 'build', view: 'closeup', distance: 70, shot: 'mid', shotMode: 'render' },
  ];
  kinds.forEach((k, i) => {
    steps.push({ do: 'lab', op: 'grime', focus: k, index: 2 + i, margin: 6 });
    steps.push({ do: 'camera', target: 'build', view: 'closeup', distance: 24, shot: 'close-' + k, shotMode: 'render' });
  });
  return { id: `${layer}-${preset}`, site: 'flat', pace: 'settled', land, steps };
});
const out = {
  _about: 'Every surviving industrial style with its marks of work (made by tools/suite/make-industrial-scenarios.js; docs/runbooks/industrial.md). Send with: powershell -File tools/suite/Send-Suite.ps1 -File suite/scenarios/ground-industrial-all.json -Who makeover-industrial',
  scenarios,
};
const file = path.join(__dirname, '..', '..', 'suite', 'scenarios', ground === 'flat' && size[0] === 220 && only.length === 0 ? 'ground-industrial-all.json' : only.length ? 'ground-industrial-some.json' : `ground-industrial-all-${size.join('x')}-${ground}.json`);
fs.writeFileSync(file, JSON.stringify(out, null, 2) + '\n');
console.log(file, scenarios.length, 'scenarios');
