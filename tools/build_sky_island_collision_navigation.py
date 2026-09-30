"""Build collision-aware SkyIsland navigation from Blender rigid-surface slices.

Requires Shapely 2.1+. This is an author-time bake; no runtime physics scans.
The design layout remains unchanged. The sidecar records source hashes, exact
bridge heights, conservative rigid footprints and a connected mesh below 4095 vertices.
Never deploy a candidate without the Unity PhysX and gameplay-gate checks.
"""
import sys,json,math,time,hashlib,argparse
from collections import defaultdict
from pathlib import Path
ROOT=Path(__file__).resolve().parents[1]
sys.path.insert(0,str(ROOT/'tools'))
from shapely import Polygon,LineString,Point,MultiPoint,unary_union,set_precision,constrained_delaunay_triangles
from sky_island_navigation import MeshBuilder,triangle_height
parser=argparse.ArgumentParser(description=__doc__)
parser.add_argument('--layout',type=Path,default=ROOT/'ArtSource/SkyIsland/layout.json')
parser.add_argument('--footprints',type=Path,required=True)
parser.add_argument('--output',type=Path,default=ROOT/'ArtSource/SkyIsland/collision_navigation.json')
parser.add_argument('--physics-report',type=Path)
args=parser.parse_args();root=ROOT;layout=json.loads(args.layout.read_text(encoding='utf-8-sig'))
source=json.loads(args.footprints.read_text(encoding='utf-8'))
byregion=defaultdict(list);object_shapes={};object_regions={}
for index,row in enumerate(source['objects']):
 shapes=[]
 for coords in row['polygons']:
  if len(set(map(tuple,coords)))<2:continue
  shape=Polygon(coords) if len(coords)>2 else LineString(coords)
  if not shape.is_valid or shape.area<1e-10:shape=MultiPoint(coords).convex_hull
  if not shape.is_empty:shapes.append(shape)
 shape=unary_union(shapes)
 byregion[row['region']].append(shape)
 object_shapes[row['name']]=shape;object_regions[row['name']]=row['region']
 if index%40==0:print('footprint',index,len(source['objects']),flush=True)
raw={k:unary_union(v) for k,v in byregion.items()}
nav=layout['navigation'];islands={i['id']:i for i in layout['islands']}
base=defaultdict(list);interfaces=defaultdict(set);edges=defaultdict(list)
for face,region in zip(nav['triangles'],nav['triangleRegions']):
 base[region].append(Polygon([(nav['vertices'][i][0],nav['vertices'][i][2]) for i in face]))
 for a,b in zip(face,face[1:]+face[:1]):edges[tuple(sorted((a,b)))].append(region)
for (a,b),regions in edges.items():
 if len(set(regions))==2:
  for region in regions:
   if region in islands:
    interfaces[region].update((nav['vertices'][i][0],nav['vertices'][i][2]) for i in (a,b))
base={k:unary_union(v) for k,v in base.items()}

def members(g):
 if g.geom_type=='Polygon':return [g]
 return [p for p in getattr(g,'geoms',[]) if p.geom_type=='Polygon']

def insert(ring,points):
 coords=list(ring.coords)[:-1];out=[]
 for a,b in zip(coords,coords[1:]+coords[:1]):
  out.append(a);dx=b[0]-a[0];dz=b[1]-a[1];length=dx*dx+dz*dz
  if length<1e-14:continue
  found=[]
  for p in points:
   t=((p[0]-a[0])*dx+(p[1]-a[1])*dz)/length
   if 1e-7<t<1-1e-7 and math.hypot(p[0]-a[0]-dx*t,p[1]-a[1]-dz*t)<.00002:found.append((t,p))
  out.extend(p for _,p in sorted(found))
 return out

# Fill each physically connected rigid component conservatively for NPC clearance.
# Disconnected columns retain their open passage; footprints never remove wall collision.
compact={}
def pieces(shape):
    if shape.geom_type in ('Polygon','LineString','Point'):return [shape]
    return [part for item in shape.geoms for part in pieces(item)]
for key,shape in raw.items():
    near=shape.intersection(base[key].buffer(.455,quad_segs=4))
    components=pieces(near.buffer(.001,quad_segs=1,join_style=2))
    compact[key]=unary_union([component.convex_hull if max(component.bounds[2]-component.bounds[0],component.bounds[3]-component.bounds[1])<8 else component for component in components
                              if component.distance(base[key])<.47])
precise_solids=set()
if args.output.exists():
 precise_solids.update(json.loads(args.output.read_text(encoding='utf-8')).get('preciseSolids',[]))
if args.physics_report:
 failures=json.loads(args.physics_report.read_text(encoding='utf-8-sig')).get('failures',[])
 for failure in failures:
  if failure['kind'] in ('navigation_blocked','navigation_sweep_blocked'):
   name=(failure.get('blocker') or '').removeprefix('COL_Wall_Mesh_')
   if name in object_shapes:precise_solids.add(name)
if not precise_solids.issubset(object_shapes):
 raise RuntimeError('A precision-tagged component changed; review the navigation bake inputs')
for tolerance in (.80,.90,1.0,1.1):
 blocked={k:v.buffer(.50+tolerance,quad_segs=1,cap_style=3,join_style=2,mitre_limit=5).simplify(tolerance,preserve_topology=True) for k,v in compact.items()}
 for name in precise_solids:
  region=object_regions[name]
  exact=object_shapes[name].buffer(.52/math.cos(math.pi/32),quad_segs=8,join_style=2,mitre_limit=5)
  blocked[region]=blocked[region].union(exact)
 design=unary_union(list(base.values()))
 full=design.difference(unary_union(list(blocked.values())))
 # Restore narrow but physically clear approach corridors around required anchors.
 # This uses the original sliced rigid geometry, not the coarse obstacle hulls.
 refinements=[]
 for marker in layout['markers']:
  kind=marker['kind'];p=marker['position'];point=Point(p[0],p[2])
  limit=.00003 if kind in ('spawn','enemy_spawn','extraction','extraction_alias','relay') else 2.25
  if kind=='region' or full.distance(point)<=limit:continue
  region=marker.get('island')
  if region not in raw:continue
  local=point.buffer(4.5,quad_segs=8)
  fine_blocked=raw[region].buffer(.49,quad_segs=8,join_style=2,mitre_limit=5)
  patch=design.intersection(local).difference(fine_blocked)
  full=full.union(patch)
  blocked[region]=blocked[region].difference(patch)
  refinements.append(marker['id'])
 spawn=next(m['position'] for m in layout['markers'] if m['id']=='PlayerSpawn')
 connected=next(p for p in members(full) if p.covers(Point(spawn[0],spawn[2])))
 bridge_pieces=[];portal_points=set();plane_groups=[]
 for index,(face,region) in enumerate(zip(nav['triangles'],nav['triangleRegions'])):
  if region in islands:continue
  coords=[nav['vertices'][i] for i in face];a,b,c=coords
  u=[b[i]-a[i] for i in range(3)];v=[c[i]-a[i] for i in range(3)]
  normal=(u[1]*v[2]-u[2]*v[1],u[2]*v[0]-u[0]*v[2],u[0]*v[1]-u[1]*v[0]);size=math.sqrt(sum(n*n for n in normal));normal=tuple(n/size for n in normal)
  group=next((g for g in plane_groups if g['region']==region and max(abs(p[1]-triangle_height((p[0],p[2]),g['coords'])) for p in coords)<.00002 and sum((normal[i]-g['normal'][i])**2 for i in range(3))<1e-12),None)
  if group is None:
   group={'region':region,'coords':coords,'normal':normal,'shapes':[],'index':index};plane_groups.append(group)
  group['shapes'].append(Polygon([(p[0],p[2]) for p in coords]))
 for group in plane_groups:
  shape=unary_union(group['shapes']).intersection(connected).simplify(.00002,preserve_topology=True)
  for poly in members(shape):
   if poly.area<1e-8:continue
   bridge_pieces.append((group['index'],group['region'],group['coords'],poly))
   portal_points.update(tuple(p) for p in poly.exterior.coords)
 mesh=MeshBuilder();sourcefaces=[]
 for region,shape in base.items():
  if region not in islands:continue
  safe=shape.intersection(connected).simplify(.00002,preserve_topology=True)
  for poly in members(safe):
   if poly.area<.001:continue
   poly=Polygon(insert(poly.exterior,portal_points),[insert(r,portal_points) for r in poly.interiors])
   for tri in constrained_delaunay_triangles(poly).geoms:
    coords=list(tri.exterior.coords)[:3]
    if tri.area<1e-8:continue
    mesh.add([(x,islands[region]['height'],z) for x,z in coords],region);sourcefaces.append(-1)
 for index,region,coords,poly in bridge_pieces:
  poly=Polygon(insert(poly.exterior,portal_points),[insert(r,portal_points) for r in poly.interiors])
  for tri in constrained_delaunay_triangles(poly).geoms:
   points=list(tri.exterior.coords)[:3]
   before=len(mesh.triangles)
   mesh.add([(x,triangle_height((x,z),coords),z) for x,z in points],region)
   if len(mesh.triangles)>before:sourcefaces.append(nav['sourceGroundFaces'][index])
 out=mesh.export();print('CANDIDATE',tolerance,len(out['vertices']),len(out['triangles']),flush=True)
 if len(out['vertices'])<4095:break
# Graph connectivity is checked independently of polygon union.
links=defaultdict(list)
for i,face in enumerate(out['triangles']):
 for a,b in zip(face,face[1:]+face[:1]):links[tuple(sorted((a,b)))].append(i)
adj=defaultdict(set)
for uses in links.values():
 if len(uses)==2:adj[uses[0]].add(uses[1]);adj[uses[1]].add(uses[0])
seen={0};pending=[0]
while pending:
 for v in adj[pending.pop()]:
  if v not in seen:seen.add(v);pending.append(v)
print('CONNECTED',len(seen),len(out['triangles']))
print('EXACT MARKERS',[(m['id'],connected.distance(Point(m['position'][0],m['position'][2]))) for m in layout['markers'] if m['kind'].lower() in ('spawn','enemy','exit','relay')])
if len(out['vertices'])>=4095 or len(seen)!=len(out['triangles']):
 raise RuntimeError('Refusing an over-budget or disconnected navigation mesh')
for marker in layout['markers']:
 if marker['kind'] in ('spawn','enemy_spawn','extraction','extraction_alias','relay'):
  p=marker['position']
  if connected.distance(Point(p[0],p[2]))>.00003:
   raise RuntimeError('Required footing disconnected: '+marker['id'])
report={'version':1,'sourceCollisionSha256':source['sourceCollisionSha256'],'sourceLayoutSha256':hashlib.sha256(args.layout.read_bytes()).hexdigest(),'navigation':out,'preciseSolids':sorted(precise_solids),'approachRefinements':refinements,'planarRegions':{k:v['height'] for k,v in islands.items()},'obstacleBuffer':.50+tolerance,'simplificationTolerance':tolerance,'sourceSolids':source['signatureSolids'],'connectedTriangles':len(seen),'blockedFootprints':{k:json.loads(__import__('shapely').to_geojson(v)) for k,v in blocked.items()}}
args.output.parent.mkdir(parents=True,exist_ok=True)
staging=args.output.with_suffix(args.output.suffix+'.candidate')
staging.write_text(json.dumps(report,separators=(',',':')),encoding='utf-8')
staging.replace(args.output)
