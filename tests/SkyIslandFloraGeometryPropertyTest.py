"""Real generated flora: manifold parts, finite area and unchanged planting radius."""
from collections import Counter
import copy
import math
from pathlib import Path
import sys

ROOT=Path(__file__).resolve().parents[1]
sys.path.insert(0,str(ROOT/'tools'))
from sky_island_nature_assets import ASSET_DIRECTORY,load_model,MeshPart


def original_radius(name):
    vertices=[]
    for line in (ASSET_DIRECTORY/(name+'.obj')).read_text(encoding='utf-8-sig').splitlines():
        row=line.split()
        if row and row[0]=='v':
            vertices.append(tuple(float(x) for x in row[1:4]))
    lo=[min(v[a] for v in vertices) for a in range(3)]
    hi=[max(v[a] for v in vertices) for a in range(3)]
    return max(math.hypot(v[0]-(lo[0]+hi[0])*.5,v[2]-(lo[2]+hi[2])*.5) for v in vertices)/(hi[1]-lo[1])


def check(name,parts):
    vertices=[v for part in parts for v in part.verts]
    assert all(math.isfinite(c) for v in vertices for c in v),'nonfinite vertex'
    assert abs(min(v[1] for v in vertices))<1e-8 and abs(max(v[1] for v in vertices)-1)<1e-8,'height changed'
    radius=max(math.hypot(v[0],v[2]) for v in vertices)
    assert abs(radius-original_radius(name))<1e-8,'planting radius changed'
    triangles=sum(len(p.faces) for p in parts)
    assert triangles<=520,'small flora triangle budget exceeded'
    for part in parts:
        edges=Counter()
        for face in part.faces:
            assert len(face)==3 and len(set(face))==3,'degenerate triangle index'
            a,b,c=(part.verts[i] for i in face)
            u=tuple(b[i]-a[i] for i in range(3));v=tuple(c[i]-a[i] for i in range(3))
            cross=(u[1]*v[2]-u[2]*v[1],u[2]*v[0]-u[0]*v[2],u[0]*v[1]-u[1]*v[0])
            assert sum(x*x for x in cross)>1e-20,'zero area face'
            for x,y in zip(face,face[1:]+face[:1]):
                edges[tuple(sorted((x,y)))]+=1
        assert all(count==2 for count in edges.values()),'open or nonmanifold small flora'
    return triangles


def main():
    names=sorted(p.stem for p in ASSET_DIRECTORY.glob('flower_*.obj'))+['grass_leafs','plant_flatShort','plant_flatTall']
    assert len(names)>=12,'missing flora source assets'
    counts={name:check(name,load_model(name)) for name in names}
    original=load_model(names[0]);first=original[0]
    probes=[]
    bad=list(first.faces);bad[0]=(bad[0][0],bad[0][0],bad[0][2])
    probes.append(('degenerate triangle index',(MeshPart(first.material,first.verts,tuple(bad)),)+original[1:]))
    probes.append(('open or nonmanifold small flora',(MeshPart(first.material,first.verts,first.faces[1:]),)+original[1:]))
    scaled=tuple(MeshPart(p.material,tuple((v[0]*1.1,v[1],v[2]*1.1) for v in p.verts),p.faces) for p in original)
    probes.append(('planting radius changed',scaled))
    for expected,parts in probes:
        try:
            check(names[0],parts)
        except AssertionError as e:
            assert str(e)==expected,(expected,str(e))
        else:
            raise AssertionError('Mutation was not rejected: '+expected)
    assert load_model(names[0])==original,'mutation contaminated production cache'
    print('PASS flora: %d models; %d-%d triangles; 3 negative probes; immutable source restored'%(len(names),min(counts.values()),max(counts.values())))


if __name__=='__main__':
    main()
