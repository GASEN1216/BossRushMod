// Record the production atlas canvas operations for the offline PNG repair.
import assert from 'node:assert/strict';
import {readFileSync, writeFileSync} from 'node:fs';
import {resolve} from 'node:path';
import {pathToFileURL} from 'node:url';

const [design, capture] = process.argv.slice(2);
const {layout, railX} = await import(pathToFileURL(resolve(design, 'proto/layout.mjs')));
const source = readFileSync(resolve(design, 'v2/js/terrain.js'), 'utf8');
const program = source.slice(source.indexOf('function rnd('), source.indexOf('function grit('));

function record(clean) {
 const commands = [];
 let path, dash = [], base;
 const context = {
  createImageData(w, h) { return {data:new Uint8ClampedArray(w*h*4)}; },
  putImageData(image, x, y) { assert.equal(x+y,0); base=image.data; },
  setTransform(...matrix) { assert.deepEqual(matrix,[8,0,0,8,672,528]); },
  fillRect(...rect) { commands.push(['rect',this.fillStyle,rect]); },
  setLineDash(values) { dash=values; },
  beginPath() { path=[]; },
  moveTo(...p) { path.push(['M',...p]); },
  lineTo(...p) { path.push(['L',...p]); },
  bezierCurveTo(...p) { path.push(['C',...p]); },
  ellipse(...p) { path.push(['E',...p]); },
  fill() { commands.push(['fill',this.fillStyle,path]); },
  stroke() { commands.push(['stroke',this.strokeStyle,path,this.lineWidth,dash]); },
 };
 const document={createElement:()=>({getContext:()=>context})};
 const T={CanvasTexture:class {}};
 new Function('document','T','railX','layout','clean',program+'\nreturn atlas(layout,clean);')(document,T,railX,layout,clean);
 return {commands,base};
}

const before=record(false), after=record(true);
const removedPaint=new Set(['rgba(43,52,47,.27)','rgba(45,48,42,.18)','rgba(35,42,39,.048)']);
assert.deepEqual(before.base,after.base);
assert.deepEqual(before.commands.filter(c=>!removedPaint.has(c[1])),after.commands);
const types=Object.fromEntries([...removedPaint].map(color=>[color,before.commands.filter(c=>c[1]===color).length]));
assert(Object.values(types).every(n=>n>0));
writeFileSync(capture,JSON.stringify({commands:before.commands,removedPaint:[...removedPaint],types}));
writeFileSync(capture+'.rgba',before.base);
