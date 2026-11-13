// 潮闸镇建筑碰撞：重跑原 three.js 建筑生成器，逐个记下墙体、家具、外梯与外墙管道的包围盒，不改任何几何。
// 2026-11-12 玩家反馈「分不清哪些有实体」「莫名其妙卡脚」：此前墙碰撞是 1 m 逻辑网格方盒（可见墙只有 0.3–0.5 m，
// 室内靠墙一圈是空气墙）、船屋敞口一侧整面空气墙、家具全无实体、外梯只烘了悬空扶手。这里给 Blender 组装
// （tools/build_tidegate_blend.py）一份贴合可见模型的盒子清单，规范见 SkyIsland/AGENTS.md §6.1。
// 从仓库根运行：node tools/build_tidegate_building_colliders.mjs（复用 Build/tidegate-walls 里的 three 依赖）
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
// 只要几何，不在 Node 里画材质贴图
kit = kit.slice(0, kit.indexOf('  let m;')) + '  let m = new THREE.MeshStandardMaterial(); m.name = kind;\n'
  + kit.slice(kit.indexOf('  return (MC[kind]'));
kit = 'export const CAP = [];\nlet CAP_TAG = null;\nexport const capTag = t => { CAP_TAG = t; };\n' + kit;
kit = once(kit, 'this.parts.get(m).push(g); return this;',
  `if (CAP_TAG) {
    g.computeBoundingBox();
    const b = g.boundingBox;
    let part = null;
    if (CAP_TAG.startsWith('furn:')) {
      const frame = mtx ? this.F.clone().multiply(mtx) : this.F;
      const yaw = Math.atan2(-frame.elements[2], frame.elements[0]);
      const inverse = new THREE.Matrix4().makeRotationY(-yaw);
      const points = g.getAttribute('position');
      const local = new THREE.Box3();
      const vertex = new THREE.Vector3();
      for (let i = 0; i < points.count; i++) local.expandByPoint(vertex.fromBufferAttribute(points, i).applyMatrix4(inverse));
      const center = local.getCenter(vertex).applyMatrix4(new THREE.Matrix4().makeRotationY(yaw));
      part = { center: center.toArray(), size: local.getSize(new THREE.Vector3()).toArray(), yaw };
    }
    CAP.push([CAP_TAG, b.min.x, b.min.y, b.min.z, b.max.x, b.max.y, b.max.z, part]);
  } this.parts.get(m).push(g); return this;`);
// 结构墙（含金库断墙、船屋敞口一侧的立柱）
kit = once(kit, '  buildWalls(c); buildLining(c); buildFloor(c);',
  "  capTag('wall'); buildWalls(c); capTag(null); buildLining(c); buildFloor(c);");
// 家具：保留原始零件边界，用于桌下空隙、控制台前沿和瓦砾间隙。
kit = once(kit, '        c.R.push(sideFrame(c, side, x + it.w / 2, t + 0.04, 0, true)); it.f(c.R, r); c.R.pop();',
  "        capTag('furn:' + list[k % list.length] + ':' + CAP.length); c.R.push(sideFrame(c, side, x + it.w / 2, t + 0.04, 0, true)); it.f(c.R, r); c.R.pop(); capTag(null);");
// 外梯：整段当一块实体（踏步本来就没有碰撞、上不去，只烘扶手会在齐身高处挂人）
{
  const start = kit.indexOf('function steelStair('), end = kit.indexOf('function antennaMast(', start);
  if (start < 0 || end < 0) throw Error('Missing steelStair source');
  let block = kit.slice(start, end);
  block = once(block, '  const sc = C(', "  capTag('stair:' + side); const sc = C(");
  block = once(block, '  R.pop();\n}', '  R.pop(); capTag(null);\n}');
  kit = kit.slice(0, start) + block + kit.slice(end);
}
// 穿墙管道：室外弯头落地那一截是齐身高的实物
{
  const start = kit.indexOf('function pipeThrough('), end = kit.indexOf('// 通风百叶', start);
  if (start < 0 || end < 0) throw Error('Missing pipeThrough source');
  let block = kit.slice(start, end);
  block = once(block, '  R.push(sideFrame(c, side, along, 0, y));', "  capTag('pipe'); R.push(sideFrame(c, side, along, 0, y));");
  block = once(block, '  R.pop();\n}', '  R.pop(); capTag(null);\n}');
  kit = kit.slice(0, start) + block + kit.slice(end);
}
kit = once(kit, '  extras(c, ri);', '  extras(c, ri); return null;');
fs.writeFileSync(tmp + '/collider-capture.mjs', kit);
// 招牌 / 标语是画布贴图，这里只要几何：给一块什么都不画的假画布
if (typeof globalThis.document === 'undefined') {
  const noop = new Proxy(function () {}, { get: (t, k) => (k === 'measureText' ? () => ({ width: 1 }) : noop), apply: () => noop, set: () => true });
  globalThis.document = { createElement: () => ({ width: 1, height: 1, getContext: () => noop, style: {} }) };
}
const { makeBuilding, CAP } = await import(pathToFileURL(tmp + '/collider-capture.mjs').href);

const layout = JSON.parse(fs.readFileSync('ArtSource/Tidegate/export/tidegate_layout.json', 'utf8'));
const hash = (i, j) => {
  let h = (Math.imul(i | 0, 374761393) + Math.imul(j | 0, 668265263)) | 0;
  h = Math.imul(h ^ (h >>> 13), 1274126177);
  return ((h ^ (h >>> 16)) >>> 0) / 4294967295;
};
const r3 = v => Math.round(v * 1000) / 1000;
const union = (a, b) => [Math.min(a[0], b[0]), Math.min(a[1], b[1]), Math.min(a[2], b[2]), Math.max(a[3], b[3]), Math.max(a[4], b[4]), Math.max(a[5], b[5])];
const dims = b => [b[3] - b[0], b[4] - b[1], b[5] - b[2]];
// 两个盒子在水平面上几乎重合（同一段墙的上下两截、断墙的低段 + 起伏顶 + 水位线色带）就并成一块
const sameFootprint = (a, b) => {
  const ix = Math.min(a[3], b[3]) - Math.max(a[0], b[0]), iz = Math.min(a[5], b[5]) - Math.max(a[2], b[2]);
  if (ix <= 0 || iz <= 0) return false;
  const area = q => (q[3] - q[0]) * (q[5] - q[2]);
  return ix * iz >= 0.85 * Math.max(area(a), area(b));
};
const buildings = {};
let walls = 0, furniture = 0, stairs = 0, pipes = 0;
for (const b of layout.buildings) {
  const [x0, z0, x1, z1] = b.footprint, cx = (x0 + x1) / 2, cz = (z0 + z1) / 2;
  const doors = b.doors.map(([side, pos, width]) => ({ side, at: side === 'n' || side === 's' ? pos - cx : pos - cz, width: width || 2 }));
  CAP.length = 0;
  makeBuilding({ w: x1 - x0, d: z1 - z0, doors, style: b.style, stories: b.stories, seed: hash(x0, z0) * 1e6 | 0 });
  const world = row => [row[1] + cx, row[2] + b.floorY, row[3] + cz, row[4] + cx, row[5] + b.floorY, row[6] + cz];
  const out = { style: b.style, walls: [], furniture: [], stairs: [], pipes: [], wallDetails: [], pipeDetails: [] };
  const groups = new Map();
  for (const row of CAP) {
    const tag = row[0], box = world(row), d = dims(box);
    if (tag === 'wall' || tag === 'pipe') {
      if (Math.min(...d) < 0.002) continue;
      // 保持旧结构盒的 ID。旧包省掉的门楣和细管另发独立的盒。
      if (box[1] - b.floorY > 2.0 || Math.min(d[0], d[2]) < 0.02 || Math.max(...d) < 0.25) {
        (tag === 'wall' ? out.wallDetails : out.pipeDetails).push(box);
        continue;
      }
      const list = tag === 'wall' ? out.walls : out.pipes;
      const hit = list.find(q => sameFootprint(q, box));
      if (hit) list.splice(list.indexOf(hit), 1, union(hit, box)); else list.push(box);
    } else {
      if (!groups.has(tag)) groups.set(tag, []);
      const part = row[7];
      groups.get(tag).push({ box, part: part && {
        center: [part.center[0] + cx, part.center[1] + b.floorY, part.center[2] + cz].map(r3),
        size: part.size.map(r3), yaw: Math.round(part.yaw * 10000) / 10000,
      } });
    }
  }
  for (const [tag, rows] of groups) {
    const box = rows.map(r => r.box).reduce(union);
    if (tag.startsWith('furn:')) {
      const name = tag.split(':')[1];
      const entry = { name, box: box.map(r3) };
      const parts = rows.map(r => r.part);
      // 纸片、按钮和屏幕贴面由承载实体覆盖，桌板上的终端保留自己的实体。
      if (name === 'desk') entry.parts = parts.filter((_, i) => i !== 3);
      if (name === 'console') entry.parts = [parts[0], parts[1], parts[2], parts.at(-1)];
      if (name === 'rubble') entry.parts = parts;
      out.furniture.push(entry);
    } else if (tag.startsWith('stair:')) out.stairs.push(box);
  }
  out.walls = out.walls.map(q => q.map(r3)); out.pipes = out.pipes.map(q => q.map(r3));
  out.wallDetails = out.wallDetails.map(q => q.map(r3)); out.pipeDetails = out.pipeDetails.map(q => q.map(r3));
  out.stairs = out.stairs.map(q => q.map(r3));
  walls += out.walls.length; furniture += out.furniture.length; stairs += out.stairs.length; pipes += out.pipes.length;
  if (out.walls.length === 0) throw Error('No wall boxes captured: ' + b.id);
  buildings[b.id] = out;
}
if (stairs !== 3) throw Error('Exterior stair count changed: ' + stairs);
const result = { schema: 2, coordinates: 'three.js（y 朝上；Unity z 取反）', sourceSha256: createHash('sha256').update(original).digest('hex'), buildings };
fs.writeFileSync('ArtSource/Tidegate/fbx/tidegate_building_colliders.json', JSON.stringify(result));
console.log('TIDEGATE_BUILDING_COLLIDERS_OK buildings=' + Object.keys(buildings).length + ' walls=' + walls + ' furniture=' + furniture + ' stairs=' + stairs + ' pipes=' + pipes);
