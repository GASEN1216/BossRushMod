// Record the original generator's structural triangles without re-authoring any geometry.
// From repo root: npm --prefix Build/tidegate-walls install --no-save three@0.160.0 && node tools/build_tidegate_wall_faces.mjs
import fs from 'node:fs'; import path from 'node:path'; import { pathToFileURL } from 'node:url';
const tmp=path.resolve('Build/tidegate-walls'), src='docs/design/tidegate-town/v2/js/';
fs.mkdirSync(tmp,{recursive:true});
const once=(s,a,b)=>{if(s.split(a).length!==2)throw Error('Source signature changed: '+a);return s.replace(a,b);};
fs.copyFileSync(src+'mats.js',tmp+'/mats.mjs');
let kit=fs.readFileSync(src+'kit-buildings.js','utf8').replace("'./mats.js'","'./mats.mjs'");
// Only renderer identity and position are needed; do not render textures into browser canvases.
kit=kit.slice(0,kit.indexOf('  let m;'))+"  let m = new THREE.MeshStandardMaterial(); m.name = kind;\n"+kit.slice(kit.indexOf('  return (MC[kind]'));
kit=once(kit,'this.parts.get(m).push(g); return this;', 'if(this.capture) this.wallFaces.push(g); this.parts.get(m).push(g); return this;');
kit=once(kit,'  buildWalls(c); buildLining(c); buildFloor(c);','  c.R.wallFaces=[]; c.R.capture=true; buildWalls(c); buildLining(c); c.R.capture=false; buildFloor(c);');
kit=once(kit,'  if (!st.ruin) facadeTrim(c);','  c.R.capture=true; if (!st.ruin) facadeTrim(c); c.R.capture=false;');
// Keep the original RNG and exact order of door/awning generation, but only windows join the wall.
kit=once(kit,'  placeWindows(c);','  c.R.capture=true; placeWindows(c); return c.R.wallFaces;');
fs.writeFileSync(tmp+'/walls.mjs',kit);
const { makeBuilding }=await import(pathToFileURL(tmp+'/walls.mjs').href);
const layout=JSON.parse(fs.readFileSync('ArtSource/Tidegate/export/tidegate_layout.json','utf8'));
const hash=(i,j)=>{let h=(Math.imul(i|0,374761393)+Math.imul(j|0,668265263))|0;h=Math.imul(h^(h>>>13),1274126177);return((h^(h>>>16))>>>0)/4294967295;};
const all={};for(const b of layout.buildings){
  const[x0,z0,x1,z1]=b.footprint,cx=(x0+x1)/2,cz=(z0+z1)/2;
  const doors=b.doors.map(([side,pos,width])=>({side,at:side==='n'||side==='s'?pos-cx:pos-cz,width:width||2}));
  const faces=makeBuilding({w:x1-x0,d:z1-z0,doors,style:b.style,stories:b.stories,seed:hash(x0,z0)*1e6|0});
  const tris=[]; for(const face of faces){const v=face.attributes.position.array;for(let i=0;i<v.length;i+=9)tris.push(Array.from(v.slice(i,i+9)));} all[b.id]=tris;
}
fs.writeFileSync('ArtSource/Tidegate/fbx/tidegate_wall_faces.json',JSON.stringify(all));
console.log(Object.keys(all).length+' buildings, '+Object.values(all).reduce((t,v)=>t+v.length,0)+' declared structural triangles');
