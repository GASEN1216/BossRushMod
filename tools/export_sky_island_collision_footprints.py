"""Blender/MCP authoring export for collision-aware navigation; call export()."""
import bpy
import json
from pathlib import Path
from sky_island_collision_navigation import collision_signature, production_solid_names, read_collision_policy


def export(project,layout_path,output_path):
    layout=json.loads(Path(layout_path).read_text(encoding='utf-8-sig'))
    islands={i['id']:i for i in layout['islands']}
    # Every production hard mesh contributes to invalidation, including bridges.
    # Only island groups have a constant floor height suitable for these slices.
    names=production_solid_names(bpy.data.objects,read_collision_policy(project))
    slice_names=[name for name in names if name.split('_')[1] in islands]
    signature=collision_signature(bpy.data.objects,names)
    def clip(poly,height,sign):
     result=[]
     for a,b in zip(poly,poly[1:]+poly[:1]):
      da=(a[1]-height)*sign;db=(b[1]-height)*sign
      if da>=0:result.append(a)
      if (da>=0)!=(db>=0):
       t=da/(da-db);result.append(tuple(a[j]+(b[j]-a[j])*t for j in range(3)))
     return result
    records=[]
    for name in slice_names:
     region=name.split('_')[1]
     obj=bpy.data.objects.get(name)
     if obj is None:raise RuntimeError('Missing validated solid source '+name)
     mesh=obj.data;mesh.calc_loop_triangles();world=[obj.matrix_world @ v.co for v in mesh.vertices]
     verts=[(float(v.x),float(v.z),float(v.y)) for v in world];y=islands[region]['height'];polygons=[]
     for tri in mesh.loop_triangles:
      face=tuple(tri.vertices);poly=[verts[i] for i in face]
      if max(v[1] for v in poly)<y+.15 or min(v[1] for v in poly)>y+1.95:continue
      poly=clip(clip(poly,y+.15,1),y+1.95,-1)
      if len(poly)>=2:polygons.append([[round(v[0],6),round(v[2],6)] for v in poly])
     if polygons:records.append({'name':name,'region':region,'polygons':polygons})
    output={'sourceCollisionSha256':signature,'capsuleRadius':.45,'band':[.15,1.95],'objects':records,'signatureSolids':names,'sliceSolids':slice_names}
    Path(output_path).write_text(json.dumps(output,separators=(',',':')),encoding='utf-8')
    return {'path':str(output_path),'objects':len(records),'slicedFaces':sum(len(r['polygons']) for r in records),'collisionSha256':signature}
