"""Separate verified exposed Tidegate surface groups without changing colliders or shaders.

Run after a Unity rebuild and before deployment:
    python tools/repair_tidegate_surfaces.py input_bundle repaired_bundle
    python tools/repair_tidegate_surfaces.py repaired_bundle --check

The tool accepts only exact authored nodes, local Float32 positions and known world
heights. It never overwrites input, edits a shared mesh or changes other objects.
This is a serialized-bundle correction, not a Unity/Blender rebuild.
"""
from pathlib import Path
from collections import Counter
import argparse
import copy
import hashlib
import json
import sys
import numpy as np
import UnityPy
from UnityPy.helpers.MeshHelper import MeshHandler
from rebuild_lastshift_bundle_layers import transform, key, save_bundle, verified_changed_objects

PREFIX = '/TidegateWorld/Visual/'
DELTA = 0.004
EPS = 0.00015
# Optional x/z bounds restrict one instance in a combined scatter mesh.
TARGETS = (
    ('VIS_props/PROP_container_-66_36/Mesh_39.002', 3.350006, None, 'mesh'),
    ('VIS_props/PROP_container_-80_34/Mesh_35.061', 3.350006, None, 'mesh'),
    ('VIS_props/PROP_dockCrane_-44_37/Mesh_59.002', 1.350006, None, 'mesh'),
    ('VIS_containers/LOOT_safe_77/marker.077', -0.259991, None, 'transform'),
    ('VIS_containers/LOOT_safe_9/marker.009', 1.759997, None, 'transform'),
    ('VIS_containers/LOOT_safe_11/marker.011', 1.759994, None, 'transform'),
    ('VIS_containers/LOOT_safe_35/marker.035', 1.710004, None, 'transform'),
    ('VIS_containers/LOOT_safe_68/marker.068', 2.959989, None, 'transform'),
    ('VIS_scatter/Scatter_lobsterTrap_PROTO_lobsterTrap_p0_-2_-2', (-0.619, -0.627), None, 'mesh'),
    ('VIS_scatter/Scatter_lobsterTrap_PROTO_lobsterTrap_p0_0_-1', -0.556, None, 'mesh'),
    ('VIS_scatter/Scatter_lobsterTrap_PROTO_lobsterTrap_p0_1_0', -0.552, None, 'mesh'),
    ('VIS_scatter/Scatter_lobsterTrap_PROTO_lobsterTrap_p0_-1_-1', -0.524, None, 'mesh'),
    ('VIS_scatter/Scatter_bollard_PROTO_bollard_p0_-1_-1', 0.8, (-34.0,-39.4,0.4), 'mesh'),
)

def positions(obj):
    h = MeshHandler(obj.read())
    h.process()
    v = h.src.m_VertexData
    c = v.m_Channels[0]
    if c.format != 0 or c.dimension != 3:
        raise ValueError('Expected Float32 position channel')
    stream = h.get_streams(v.m_Channels, v.m_VertexCount)[c.stream]
    raw = bytearray(v.m_DataSize)
    view = np.ndarray((v.m_VertexCount,3), '<f4', raw, stream.offset+c.offset, (stream.stride,4))
    return h, raw, view

def nodes(env):
    wanted={PREFIX+p for p,_,_,_ in TARGETS}
    found={}
    uses=Counter()
    cache={}
    for obj in env.objects:
        if obj.type.name == 'MeshFilter':
            ref=obj.read().m_Mesh
            if ref: uses[key(ref.deref())]+=1
        if obj.type.name != 'MeshRenderer': continue
        renderer=obj.read(); go=renderer.m_GameObject.read()
        comp={p.component.type.name:p.component for p in go.m_Component}
        matrix,path=transform(comp['Transform'],cache)
        if path not in wanted: continue
        if path in found or not renderer.m_Enabled or not go.m_IsActive:
            raise ValueError('Missing/duplicate/inactive target '+path)
        mesh=comp['MeshFilter'].read().m_Mesh.deref()
        found[path]=(mesh,comp['Transform'].deref(),matrix)
    if found.keys()!=wanted:
        raise ValueError('Target inventory changed: '+str(wanted-found.keys()))
    return found,uses,cache

def mask_at(world,height,region):
    levels=np.atleast_1d(height)
    mask=np.any(np.abs(world[:,1,None]-levels[None,:])<EPS,axis=1)
    if region is not None:
        x,z,r=region
        mask &= (np.abs(world[:,0]-x)<r)&(np.abs(world[:,2]-z)<r)
    return mask

def bounds(points):
    low,high=points.min(axis=0),points.max(axis=0)
    def vector(v):return dict(zip('xyz',map(float,v.astype(np.float32))))
    return {'m_Center':vector((low+high)*.5),'m_Extent':vector((high-low)*.5)}

def evaluate(env,modify):
    found,uses,cache=nodes(env)
    changed={}
    report=[]
    states=[]
    for suffix,height,region,kind in TARGETS:
        path=PREFIX+suffix
        mesh,tr,matrix=found[path]
        h,raw,local=positions(mesh)
        world=local.astype(float)@matrix[:3,:3].T+matrix[:3,3]
        old=mask_at(world,height,region);fixed=mask_at(world,np.atleast_1d(height)+DELTA,region)
        if old.any() == fixed.any():
            raise ValueError('Ambiguous or missing surface '+path)
        state='original' if old.any() else 'fixed'
        states.append(state)
        report.append({'path':path,'state':state,'vertices':int((old|fixed).sum()),'worldY':(np.atleast_1d(height)+(DELTA if state=='fixed' else 0)).tolist(),'kind':kind})
        if state=='fixed' or not modify:continue
        if kind=='mesh':
            if uses[key(mesh)]!=1: raise ValueError('Refusing shared mesh '+path)
            original=mesh.read_typetree()
            original_bytes = mesh.get_raw_data()
            if mesh.save_typetree(copy.deepcopy(original))!=original_bytes:
                raise ValueError('Cannot roundtrip mesh '+path)
            local[old]+=np.linalg.solve(matrix[:3,:3],[0,DELTA,0]).astype(np.float32)
            tree=copy.deepcopy(original)
            tree['m_VertexData']['m_DataSize']=bytes(raw)
            # Recompute enclosing bounds only when the moved vertices leave the old AABB.
            oldbounds=tree['m_LocalAABB']
            center=np.array([oldbounds['m_Center'][a] for a in 'xyz'])
            extent=np.array([oldbounds['m_Extent'][a] for a in 'xyz'])
            if np.any(local.min(axis=0)<center-extent-EPS/100) or np.any(local.max(axis=0)>center+extent+EPS/100):
                tree['m_LocalAABB']=bounds(local)
                for sub,indices in zip(tree['m_SubMeshes'],h.get_triangles()):
                    ix=np.asarray(indices).reshape(-1)+sub['baseVertex']
                    if len(ix):sub['localAABB']=bounds(local[ix])
            mesh.save_typetree(tree)
            changed[key(mesh)]={'kind':'mesh','path':path,'old':original,'new':tree,'vertexMask':np.flatnonzero(old).tolist(),'delta':np.linalg.solve(matrix[:3,:3],[0,DELTA,0]).tolist()}
        else:
            original=tr.read_typetree()
            if original['m_Children']:raise ValueError('Marker unexpectedly has children')
            source=tr.read()
            parent,_=transform(source.m_Father,cache)
            shift=np.linalg.solve(parent[:3,:3],[0,DELTA,0])
            tree=copy.deepcopy(original)
            for axis,value in zip('xyz',shift):tree['m_LocalPosition'][axis]=float(np.float32(tree['m_LocalPosition'][axis]+value))
            tr.save_typetree(tree)
            changed[key(tr)]={'kind':'transform','path':path,'old':original,'new':tree}
    # Mixed states can be intentional following a partial author rebuild; check reports them,
    # but repair refuses partial work so that unexpected source drift is reviewed.
    if modify and len(set(states))!=1:raise ValueError('Mixed source states; re-audit before repairing')
    return changed,report

def verify(source,result,expected):
    a=UnityPy.load(str(source));b=UnityPy.load(result)
    count=0
    for ident,old,new in verified_changed_objects(a,b,expected):
        wanted=expected[ident]
        ot,nt=old.read_typetree(),new.read_typetree()
        if ot!=wanted['old'] or nt!=wanted['new']:raise ValueError('Serialized fields changed unexpectedly '+wanted['path'])
        count+=1
    _,state=evaluate(b,False)
    if any(s['state']!='fixed' for s in state):raise ValueError('Repair did not remove original surface heights')
    return count,state

def main():
    parser=argparse.ArgumentParser(description=__doc__)
    parser.add_argument('source',type=Path)
    parser.add_argument('output',type=Path,nargs='?')
    parser.add_argument('--check',action='store_true')
    args=parser.parse_args()
    env=UnityPy.load(str(args.source))
    changes,states=evaluate(env,bool(args.output))
    if args.check or not args.output:
        bad=[s['path'] for s in states if s['state']!='fixed']
        print(json.dumps({'targets':states,'unfixed':bad},ensure_ascii=False,indent=2))
        if bad:raise SystemExit(1)
        print('TIDEGATE_SURFACES_OK');return
    if args.source.resolve()==args.output.resolve():parser.error('output must differ from source')
    raw=args.source.read_bytes()
    result=save_bundle(next(iter(env.files.values())),{k[0] for k in changes}) if changes else raw
    changed_count,states=verify(args.source,result,changes)
    args.output.parent.mkdir(parents=True,exist_ok=True)
    args.output.write_bytes(result)
    report={'method':'serialized bundle position/marker correction; no Unity rebuild','sourceSHA256':hashlib.sha256(raw).hexdigest(),'bundleSHA256':hashlib.sha256(result).hexdigest(),'changedObjects':changed_count,'unrelatedObjectsByteIdentical':True,'collidersNavigationMaterialsShadersUnchanged':True,'targets':states,'deltaMeters':DELTA}
    args.output.with_name(args.output.name+'_surface_report.json').write_text(json.dumps(report,ensure_ascii=False,indent=2),'utf-8')
    print(json.dumps(report,ensure_ascii=False,indent=2))
    print('TIDEGATE_SURFACES_OK')
if __name__=='__main__':main()
