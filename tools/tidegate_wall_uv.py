"""Tag actual Blender polygon corners with source ownership; walls share a material with doors/furniture."""
import itertools
import json
import sys
from collections import defaultdict
from pathlib import Path


def stamp_wall_uv(collection, layout, source):
    import numpy as np
    faces = json.loads(Path(source).read_text('utf-8-sig'))
    buildings = {b['id']: b for b in layout['buildings']}
    bins, seen = {}, defaultdict(set)
    for name, fs in faces.items():
        coords = np.asarray(fs).reshape((-1,3,3))
        table = defaultdict(list)
        for i, center in enumerate(coords.mean(axis=1)):
            table[tuple(np.floor(center*20).astype(int))].append(i)
        bins[name] = (coords, table)
    stats = defaultdict(lambda: [0,0])
    for obj in collection.all_objects:
        if obj.type != 'MESH' or obj.parent is None: continue
        room = next((b for b in buildings if obj.parent.name.startswith('BLD_'+b+'_') and not obj.parent.name.endswith('_Roof')), None)
        if room is None: continue
        b = buildings[room]; x0,z0,x1,z1 = b['footprint']
        mesh = obj.data; uv = mesh.uv_layers.get('TidegateWall') or mesh.uv_layers.new(name='TidegateWall', do_init=False)
        # Unity's mesh.uv2/TEXCOORD1; adding another earlier UV must fail loudly.
        if list(mesh.uv_layers.keys()).index('TidegateWall') != 1: raise ValueError('Expected second UV: '+obj.name)
        pts=np.asarray([tuple(obj.matrix_world @ v.co) for v in mesh.vertices])
        pts=pts[:,[0,2,1]]; pts[:,2]*=-1; pts-=np.array([(x0+x1)/2,b['floorY'],(z0+z1)/2])
        coords, buckets = bins[room]
        for p in mesh.polygons:
            ids=list(p.vertices)
            if len(ids)!=3:raise ValueError('Non-triangle '+obj.name)
            tri=pts[ids]; center=tri.mean(axis=0)
            lo=np.floor((center-0.0002)*20).astype(int); hi=np.floor((center+0.0002)*20).astype(int)
            mask=0
            for key in itertools.product(*(range(lo[k],hi[k]+1) for k in range(3))):
                for i in buckets.get(key,()):
                    delta=np.abs(tri[:,None,:]-coords[i][None,:,:]).max(axis=2)
                    if delta.min(axis=0).max()<0.0002 and delta.min(axis=1).max()<0.0002:
                        seen[room].add(i);mask=1
            for loop in p.loop_indices:uv.data[loop].uv=(mask,0)
            stats[room][mask]+=1
    missed={k:(len(fs)-len(seen[k])) for k,fs in faces.items() if len(seen[k])!=len(fs)}
    if missed: raise ValueError('Source building geometry disagrees with baked faces: '+str(missed))
    print('ROOM_WALL_UV',dict(stats))
    return stats


if __name__ == '__main__':
    import bpy
    src=Path(sys.argv[sys.argv.index('--')+1]); collection=bpy.data.collections['TG_buildings']
    stamp_wall_uv(collection,json.loads((src/'export/tidegate_layout.json').read_text('utf-8-sig')),src/'fbx/tidegate_wall_faces.json')
    bpy.ops.object.select_all(action='DESELECT')
    for obj in collection.all_objects:obj.select_set(obj.type in {'MESH','EMPTY'})
    bpy.ops.export_scene.fbx(filepath=str(src/'fbx/Tidegate_buildings.fbx'),use_selection=True,object_types={'MESH','EMPTY'},
                             axis_forward='-Z',axis_up='Y',bake_anim=False,add_leaf_bones=False,path_mode='COPY',embed_textures=True)
