// Formats the shared decal library's family files (Recipes/decals/library/*.json) the way they are written by hand:
// a kind's header keys on a few lines, one op per line. Use after a script added or changed kinds, so diffs stay small.
//   node tools/decals/format-library.js            every family
//   node tools/decals/format-library.js tone.json  one
const fs = require('fs'), path = require('path');
const dir = path.join(__dirname, '..', '..', 'Recipes', 'decals', 'library');
const inline = v => JSON.stringify(v, null, 1).replace(/\n\s*/g, ' ').replace(/\[ /g, '[').replace(/ \]/g, ']').replace(/\{ /g, '{ ').replace(/ \}/g, ' }');
const pick = (k, keys) => keys.filter(x => x in k).map(x => JSON.stringify(x) + ': ' + inline(k[x])).join(', ');
const groups = [['id', 'name', 'tier'], ['about'], ['texel', 'layers', 'order', 'lod', 'maxTexels', 'opacity', 'bump', 'saturation']];
function kind(k) {
  const lines = [], used = new Set(['ops']);
  for (const g of groups) { const s = pick(k, g); g.forEach(x => used.add(x)); if (s) lines.push(s); }
  for (const x of Object.keys(k)) if (!used.has(x)) lines.push(JSON.stringify(x) + ': ' + inline(k[x]));
  let out = '    {\n' + lines.map(l => '      ' + l).join(',\n');
  if (k.ops) out += ',\n      "ops": [\n' + k.ops.map(o => '        ' + inline(o)).join(',\n') + '\n      ]';
  return out + '\n    }';
}
for (const f of (process.argv.length > 2 ? process.argv.slice(2) : fs.readdirSync(dir).filter(f => f.endsWith('.json')))) {
  const p = path.join(dir, path.basename(f)), raw = fs.readFileSync(p, 'utf8'), j = JSON.parse(raw);
  const top = Object.keys(j).filter(x => x !== 'kinds').map(x => '  ' + JSON.stringify(x) + ': ' + inline(j[x]));
  let out = '{\n' + top.join(',\n') + ',\n  "kinds": [\n' + j.kinds.map(kind).join(',\n') + '\n  ]\n}\n';
  if (raw.includes('\r\n')) out = out.replace(/\n/g, '\r\n');
  JSON.parse(out);
  fs.writeFileSync(p, out);
  console.log(f, j.kinds.length, 'kinds');
}
