// Capture the original generator's indoor visibility semantics without changing its geometry.
// Setup once: npm --prefix Build/tidegate-walls install --no-save three@0.160.0
// Run from repository root: node tools/build_tidegate_roof_faces.mjs
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
// Canvas textures do not affect positions; material identities remain separate.
kit = kit.slice(0, kit.indexOf('  let m;')) + '  let m = new THREE.MeshStandardMaterial(); m.name = kind;\n'
  + kit.slice(kit.indexOf('  return (MC[kind]'));
kit = once(kit, 'trs, signMaterial, noise', 'trs, signMaterial as unusedSignMaterial, noise');
kit = once(kit, 'const PI =', 'const signMaterial = () => new THREE.MeshStandardMaterial();\nconst PI =');
kit = once(kit, 'this.parts.get(m).push(g); return this;',
  'if (this.structure) (this.keepIndoors ? this.contents : this.structure).push(g); this.parts.get(m).push(g); return this;');
kit = once(kit, '  buildWalls(c); buildLining(c); buildFloor(c);',
  '  c.R.structure=[]; c.R.contents=[]; c.F.structure=[]; c.F.contents=[]; buildWalls(c); buildLining(c); c.R.keepIndoors=true; buildFloor(c); c.R.keepIndoors=false;');
kit = once(kit, '  furnish(c);', '  c.R.keepIndoors=true; furnish(c); c.R.keepIndoors=false;');
// All upper facade, equipment, awnings, signs, pipes and cables stay attached to the disappearing shell.
// Only original floor/furnish calls are protected; high cupboards and racks must remain intact indoors.
kit = once(kit, '  const root = new THREE.Group(), roof = new THREE.Group();',
  '  return { structure:c.R.structure, contents:c.R.contents, upper:c.F.structure };');
fs.writeFileSync(tmp + '/roof-capture.mjs', kit);
const { makeBuilding } = await import(pathToFileURL(tmp + '/roof-capture.mjs').href);
const layout = JSON.parse(fs.readFileSync('ArtSource/Tidegate/export/tidegate_layout.json', 'utf8'));
const hash = (i, j) => {
  let h = (Math.imul(i | 0, 374761393) + Math.imul(j | 0, 668265263)) | 0;
  h = Math.imul(h ^ (h >>> 13), 1274126177);
  return ((h ^ (h >>> 16)) >>> 0) / 4294967295;
};
const buildings = {};
for (const b of layout.buildings) {
  const [x0, z0, x1, z1] = b.footprint, cx = (x0 + x1) / 2, cz = (z0 + z1) / 2;
  const doors = b.doors.map(([side, pos, width]) => ({ side, at: side === 'n' || side === 's' ? pos - cx : pos - cz, width: width || 2 }));
  const spec = { w: x1 - x0, d: z1 - z0, doors, style: b.style, stories: b.stories, seed: hash(x0, z0) * 1e6 | 0 };
  if (b.sign) spec.sign = { text:b.sign, side:doors[0].side };
  const groups = makeBuilding(spec);
  buildings[b.id] = {};
  for (const [kind, parts] of Object.entries(groups)) {
    const triangles = [];
    for (const part of parts) {
      const vertices = part.attributes.position.array;
      for (let i = 0; i < vertices.length; i += 9) triangles.push(Array.from(vertices.slice(i, i + 9)));
    }
    buildings[b.id][kind] = triangles;
  }
}
const result = { schema:2, lowWallHeight:0.9, sourceSha256:createHash('sha256').update(original).digest('hex'), buildings };
fs.writeFileSync('ArtSource/Tidegate/fbx/tidegate_roof_faces.json', JSON.stringify(result));
console.log('TIDEGATE_ROOF_FACES_OK ' + Object.entries(buildings).map(([id, groups]) => id + '=' + Object.entries(groups).map(([k,v])=>k+':'+v.length).join('/')).join(', '));
