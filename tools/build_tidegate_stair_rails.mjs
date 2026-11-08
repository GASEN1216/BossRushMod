// Capture the original building generator's exterior stair handrails only.
// Run: node tools/build_tidegate_stair_rails.mjs (uses the existing Build/tidegate-walls three dependency).
import fs from 'node:fs';
import path from 'node:path';
import { pathToFileURL } from 'node:url';
import { createHash } from 'node:crypto';

const tmp = path.resolve('Build/tidegate-walls');
const source = 'docs/design/tidegate-town/v2/js/kit-buildings.js';
const original = fs.readFileSync(source, 'utf8');
const once = (text, before, after) => {
  if (text.split(before).length !== 2) throw Error('Source signature changed: ' + before);
  return text.replace(before, after);
};
fs.mkdirSync(tmp, { recursive: true });
fs.copyFileSync('docs/design/tidegate-town/v2/js/mats.js', tmp + '/mats.mjs');
let kit = original.replace("'./mats.js'", "'./mats.mjs'");
kit = kit.slice(0, kit.indexOf('  let m;')) + '  let m = new THREE.MeshStandardMaterial(); m.name = kind;\n'
  + kit.slice(kit.indexOf('  return (MC[kind]'));
kit = once(kit, 'this.parts.get(m).push(g); return this;',
  'if (this.captureRail) this.railFaces.push(g); this.parts.get(m).push(g); return this;');
kit = once(kit, '  buildWalls(c); buildLining(c); buildFloor(c);',
  '  c.R.railFaces = []; buildWalls(c); buildLining(c); buildFloor(c);');
const start = kit.indexOf('function steelStair('), end = kit.indexOf('function antennaMast(', start);
if (start < 0 || end < 0) throw Error('Missing steelStair source');
let block = kit.slice(start, end);
block = once(block, '  R.bar(P, COL.yellow,', '  R.captureRail = true; R.bar(P, COL.yellow,');
block = once(block, '  R.pop();', '  R.captureRail = false; R.pop();');
kit = kit.slice(0, start) + block + kit.slice(end);
kit = once(kit, '  extras(c, ri);', '  extras(c, ri); return c.R.railFaces;');
fs.writeFileSync(tmp + '/stair-rail-capture.mjs', kit);
const { makeBuilding } = await import(pathToFileURL(tmp + '/stair-rail-capture.mjs').href);
const layout = JSON.parse(fs.readFileSync('ArtSource/Tidegate/export/tidegate_layout.json', 'utf8'));
const hash = (i, j) => {
  let h = (Math.imul(i | 0, 374761393) + Math.imul(j | 0, 668265263)) | 0;
  h = Math.imul(h ^ (h >>> 13), 1274126177);
  return ((h ^ (h >>> 16)) >>> 0) / 4294967295;
};
const buildings = {};
for (const b of layout.buildings.filter(b => ['pump1', 'pump2', 'ctrl'].includes(b.id))) {
  const [x0, z0, x1, z1] = b.footprint, cx = (x0 + x1) / 2, cz = (z0 + z1) / 2;
  const doors = b.doors.map(([side, pos, width]) => ({ side, at: side === 'n' || side === 's' ? pos - cx : pos - cz, width: width || 2 }));
  const parts = makeBuilding({ w: x1 - x0, d: z1 - z0, doors, style: b.style, stories: b.stories, seed: hash(x0, z0) * 1e6 | 0 });
  const triangles = [];
  for (const part of parts) {
    const vertices = part.attributes.position.array;
    for (let i = 0; i < vertices.length; i += 9) triangles.push(Array.from(vertices.slice(i, i + 9)));
  }
  if (triangles.length !== 96) throw Error('Stair handrail topology changed: ' + b.id + ' triangles=' + triangles.length);
  buildings[b.id] = { origin: [cx, b.floorY, cz], triangles };
}
if (Object.keys(buildings).length !== 3) throw Error('Missing exterior stair building');
const result = { sourceSha256: createHash('sha256').update(original).digest('hex'), buildings };
fs.writeFileSync('ArtSource/Tidegate/fbx/tidegate_stair_rails.json', JSON.stringify(result));
console.log('TIDEGATE_STAIR_RAILS_OK ' + Object.entries(buildings).map(([id, row]) => id + '=' + row.triangles.length).join(', '));
