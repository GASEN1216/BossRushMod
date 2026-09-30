"""Repair only SkyIsland collision meshes through Blender/MCP; preserve visible art.

Run inside Blender: repair(project, report_path). No game process or save access.
The main generator shares solid_island_ground(), so regeneration keeps the fix.
"""
import hashlib
import json
from pathlib import Path
import sys

import bpy
from mathutils import Vector
from mathutils.bvhtree import BVHTree

sys.path.insert(0, str(Path(__file__).resolve().parent))
from generate_sky_island import solid_island_ground


def fingerprint(objects):
    digest=hashlib.sha256()
    for obj in sorted(objects,key=lambda o:o.name):
        digest.update(obj.name.encode())
        digest.update(str(tuple(tuple(row) for row in obj.matrix_world)).encode())
        for v in obj.data.vertices:
            digest.update(str(tuple(v.co)).encode())
        for face in obj.data.polygons:
            digest.update(str(tuple(face.vertices)).encode())
    return digest.hexdigest()


def floor_probes(obj, island, obstacles):
    vertices=[obj.matrix_world @ v.co for v in obj.data.vertices]
    tree=BVHTree.FromPolygons(vertices,[tuple(p.vertices) for p in obj.data.polygons])
    misses=[]
    checked=0
    for obstacle in obstacles:
        if obstacle['island']!=island['id']:
            continue
        x,_,z=obstacle['center'];w,_,d=obstacle['size']
        for fx in (-.49,-.25,0,.25,.49):
            for fz in (-.49,-.25,0,.25,.49):
                checked+=1
                point=(x+w*fx,z+d*fz,island['height']+1)
                hit,normal,_,_=tree.ray_cast(Vector(point),Vector((0,0,-1)),2)
                if hit is None or abs(hit.z-island['height'])>0.001 or normal.z<0.99:
                    misses.append([obstacle['id'],fx,fz])
    return {'checked':checked,'misses':len(misses),'examples':misses[:12]}


def repair(project, report_path):
    project=Path(project)
    source=project/'ArtSource/SkyIsland/SkyIslandWorld.blend'
    if Path(bpy.data.filepath).resolve()!=source.resolve():
        raise RuntimeError('Open the intended authoring blend before repair: '+str(source))
    layout=json.loads((project/'Assets/SkyIsland/sky_island_layout.json').read_text(encoding='utf-8-sig'))
    unchanged=[o for o in bpy.data.objects if o.type=='MESH' and (o.name.startswith(('VIS_','NAV_')) or
                (o.name.startswith('COL_Ground_') and o.name[len('COL_Ground_'):] not in {i['id'] for i in layout['islands']}))]
    before=fingerprint(unchanged)
    floors=[]
    for island in layout['islands']:
        obj=bpy.data.objects.get('COL_Ground_'+island['id'])
        if obj is None:
            raise RuntimeError('Missing region collider: '+island['id'])
        prior=floor_probes(obj,island,layout['obstacles'])
        old_area=sum(p.area for p in obj.data.polygons)
        vv,ff=solid_island_ground(island)
        mesh=bpy.data.meshes.new(obj.name+'_Solid')
        mesh.from_pydata([(p[0],p[2],p[1]) for p in vv],[],[tuple(reversed(f)) for f in ff])
        mesh.update()
        old=obj.data;obj.data=mesh
        if old.users==0:
            bpy.data.meshes.remove(old)
        mesh.name=obj.name
        obj.hide_render=True
        obj.display_type='WIRE'
        after=floor_probes(obj,island,layout['obstacles'])
        outline=island['outline']
        expected=abs(sum(a[0]*b[1]-a[1]*b[0] for a,b in zip(outline,outline[1:]+outline[:1])))*.5
        area=sum(p.area for p in mesh.polygons)
        if abs(area-expected)>max(.005,expected*.000001) or after['misses']:
            raise RuntimeError('Incomplete physical floor: '+island['id'])
        floors.append({'region':island['id'],'before':prior,'after':after,'oldArea':old_area,
                       'area':area,'expectedArea':expected,'triangles':len(mesh.polygons)})
    # Repair winding in place: bmesh.recalc_face_normals must never collapse the
    # intentional opposite normals on two-sided open perimeter quads.
    rail=bpy.data.objects['COL_Rail_Perimeter']
    railmesh=rail.data
    if len(railmesh.polygons)%2:
        raise RuntimeError('Perimeter expects paired opposing faces')
    faces=[]
    for index in range(0,len(railmesh.polygons),2):
        face=tuple(railmesh.polygons[index].vertices)
        opposite=tuple(railmesh.polygons[index+1].vertices)
        if set(face)!=set(opposite):
            raise RuntimeError('Perimeter pair mismatch')
        faces.extend((face,tuple(reversed(face))))
    vertices=[tuple(v.co) for v in railmesh.vertices]
    new=bpy.data.meshes.new('COL_Rail_Perimeter_Repaired')
    new.from_pydata(vertices,[],faces);new.update()
    rail.data=new
    if railmesh.users==0:
        bpy.data.meshes.remove(railmesh)
    new.name=rail.name
    bad=sum(1 for i in range(0,len(new.polygons),2) if new.polygons[i].normal.dot(new.polygons[i+1].normal)>-.999)
    if bad:
        raise RuntimeError('Perimeter has one-sided face pairs')
    after_fingerprint=fingerprint(unchanged)
    if before!=after_fingerprint:
        raise RuntimeError('Unexpected visible/navigation/bridge mutation')
    report={'status':'PASS','evidence':'L2 Blender geometry and downward BVH rays',
            'blenderVersion':bpy.app.version_string,'source':str(source),
            'floors':floors,'groundProbeCount':sum(f['after']['checked'] for f in floors),
            'priorMissingGroundProbes':sum(f['before']['misses'] for f in floors),
            'missingGroundProbes':sum(f['after']['misses'] for f in floors),
            'restoredGroundArea':sum(f['area']-f['oldArea'] for f in floors),
            'perimeterSegments':len(new.polygons)//2,'opposedPerimeterFaces':bad==0,
            'unchangedVisibleNavigationBridges':before==after_fingerprint,
            'unchangedGeometrySha256':before,'gameSmoke':'Not performed'}
    report_path=Path(report_path);report_path.parent.mkdir(parents=True,exist_ok=True)
    report_path.write_text(json.dumps(report,ensure_ascii=False,indent=2),encoding='utf-8')
    bpy.ops.wm.save_as_mainfile(filepath=str(source))
    bpy.ops.object.select_all(action='DESELECT')
    for obj in bpy.context.scene.objects:
        obj.select_set(obj.type in {'MESH','EMPTY'})
    fbx=project/'Assets/SkyIsland/SkyIslandWorld.fbx'
    bpy.ops.export_scene.fbx(filepath=str(fbx),use_selection=True,object_types={'MESH','EMPTY'},
        axis_forward='-Z',axis_up='Y',bake_anim=False,add_leaf_bones=False,path_mode='RELATIVE')
    return report
