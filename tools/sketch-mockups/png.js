// tools/sketch-mockups/png.js — node png.js <file.svg|file.html> [w h]: a PNG beside it, rendered by headless Edge.
const { execFileSync } = require('child_process');
const fs = require('fs'), path = require('path');
// A fresh short profile per run: Edge fails silently on a long profile path or a profile a crashed run left behind.
const profile = path.join(require('os').tmpdir(), 'pmedge-' + process.pid);
process.on('exit', () => { try { fs.rmSync(profile, { recursive: true, force: true }); } catch (e) { } });
const EDGE = 'C:\\Program Files (x86)\\Microsoft\\Edge\\Application\\msedge.exe';
for (const a of process.argv.slice(2).filter((x) => /\.(svg|html)$/.test(x))) {
  const file = path.resolve(a);
  const txt = fs.readFileSync(file, 'utf8');
  let w = +(process.argv[3] || 0), h = +(process.argv[4] || 0);
  if (!w) { w = +(/width="(\d+)"/.exec(txt) || [0, 1000])[1]; h = +(/height="(\d+)"/.exec(txt) || [0, 800])[1]; }
  const out = file.replace(/\.(svg|html)$/, '.png');
  try {
    execFileSync(EDGE, ['--headless=new', '--disable-gpu', '--hide-scrollbars', '--force-device-scale-factor=1', '--no-first-run', '--user-data-dir=' + profile,
      `--window-size=${w},${h}`, `--screenshot=${out}`, 'file:///' + file.replace(/\\/g, '/')], { stdio: 'ignore', timeout: 60000 });
  } catch (e) { /* Edge exits non-zero sometimes even when the shot is written */ }
  console.log(fs.existsSync(out) ? out : 'FAILED ' + file);
}
