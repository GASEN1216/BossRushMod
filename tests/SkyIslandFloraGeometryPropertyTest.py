"""Flora geometry; source CI validates public envelopes and reports private OBJ/MTL PARTIAL."""
from collections import Counter
import json
import math
import os
from pathlib import Path
import sys

ROOT=Path(__file__).resolve().parents[1]
sys.path.insert(0,str(ROOT/'tools'))
from sky_island_nature_assets import ASSET_DIRECTORY,load_model,MeshPart
from sky_island_flora import refine_model

METADATA=ROOT/'ArtSource/SkyIsland/Validation/sky_island_dressing.json'
GRASSES=('grass_leafs','plant_flatShort','plant_flatTall')


def source_envelopes():
    """Public placement facts exercise the actual refiner, without pretending to be source meshes."""
    data=json.loads(METADATA.read_text(encoding='utf-8-sig'))
    assert data['version']==1,'unsupported dressing metadata'
    models=data['natureModels'];placements=data['meadowPlacements']
    assert all(isinstance(count,int) and count>0 for count in models.values()),'invalid nature model usage'
    assert len(placements)==data['natureInstances']==sum(models.values()),'nature instance count changed'
    assert Counter(p['model'] for p in placements)==models,'nature model usage does not match placements'
    radii={}
    for placement in placements:
        position=placement['position'];height=placement['height'];radius=placement['radius']
        assert len(position)==3 and all(math.isfinite(c) for c in position),'nonfinite placement position'
        assert math.isfinite(height) and height>0 and math.isfinite(radius) and radius>0,'invalid planting envelope'
        radii.setdefault(placement['model'],radius/height)
    names=sorted(name for name in models if name.startswith('flower_'))+list(GRASSES)
    assert len(names)>=12,'missing public flora model metadata'
    assert all(name in radii for name in names if name!='plant_flatTall'),'missing flora planting envelope'
    # The public meadow does not include the eight waterside Tall plants. Exercise that refiner
    # with the same public grass envelopes; its real OBJ radius is checked only in full mode.
    radii['plant_flatTall']=radii['grass_leafs']
    counts={}
    first=None
    for name in names:
        for factor in (.5,1,2):
            radius=radii[name]*factor
            envelope=(MeshPart('grass',((radius,0,0),(-radius,0,0),(0,1,0)),()),)
            refined=refine_model(name,envelope)
            assert refined is not None,'flora refiner did not recognize '+name
            parts=tuple(MeshPart(*part) for part in refined)
            counts[name]=check(name,parts,radius)
            assert envelope[0].verts==((radius,0,0),(-radius,0,0),(0,1,0)),'refiner changed input envelope'
            if first is None: first=(name,parts,radius)
    negative_probes(*first)
    return counts


def original_radius(name):
    vertices=[]
    for line in (ASSET_DIRECTORY/(name+'.obj')).read_text(encoding='utf-8-sig').splitlines():
        row=line.split()
        if row and row[0]=='v':
            vertices.append(tuple(float(x) for x in row[1:4]))
    lo=[min(v[a] for v in vertices) for a in range(3)]
    hi=[max(v[a] for v in vertices) for a in range(3)]
    return max(math.hypot(v[0]-(lo[0]+hi[0])*.5,v[2]-(lo[2]+hi[2])*.5) for v in vertices)/(hi[1]-lo[1])


def check(name,parts,expected_radius=None):
    vertices=[v for part in parts for v in part.verts]
    assert all(math.isfinite(c) for v in vertices for c in v),'nonfinite vertex'
    assert abs(min(v[1] for v in vertices))<1e-8 and abs(max(v[1] for v in vertices)-1)<1e-8,'height changed'
    radius=max(math.hypot(v[0],v[2]) for v in vertices)
    expected_radius=original_radius(name) if expected_radius is None else expected_radius
    assert abs(radius-expected_radius)<1e-8,'planting radius changed'
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


def negative_probes(name,original,expected_radius=None):
    first=original[0]
    probes=[]
    bad=list(first.faces);bad[0]=(bad[0][0],bad[0][0],bad[0][2])
    probes.append(('degenerate triangle index',(MeshPart(first.material,first.verts,tuple(bad)),)+original[1:]))
    probes.append(('open or nonmanifold small flora',(MeshPart(first.material,first.verts,first.faces[1:]),)+original[1:]))
    scaled=tuple(MeshPart(p.material,tuple((v[0]*1.1,v[1],v[2]*1.1) for v in p.verts),p.faces) for p in original)
    probes.append(('planting radius changed',scaled))
    for expected,parts in probes:
        try:
            check(name,parts,expected_radius)
        except AssertionError as e:
            assert str(e)==expected,(expected,str(e))
        else:
            raise AssertionError('Mutation was not rejected: '+expected)


def main():
    envelopes=source_envelopes()
    if os.environ.get('BOSSRUSH_GUARD_SOURCE_ONLY')=='1':
        print('SkyIslandFloraGeometryPropertyTest: PARTIAL - public placement metadata and real refiner passed '
              '(%d models, %d envelopes, 3 negative probes); private OBJ/MTL loading and original model radii not verified'
              %(len(envelopes),len(envelopes)*3))
        return 2
    names=sorted(p.stem for p in ASSET_DIRECTORY.glob('flower_*.obj'))+list(GRASSES)
    assert len(names)>=12,'missing flora source assets'
    assert set(envelopes)<=set(names),'private source assets missing a public flora model'
    missing=[name+suffix for name in names for suffix in ('.obj','.mtl') if not (ASSET_DIRECTORY/(name+suffix)).is_file()]
    assert not missing,'missing flora source assets: '+', '.join(missing)
    counts={name:check(name,load_model(name)) for name in names}
    original=load_model(names[0])
    negative_probes(names[0],original)
    assert load_model(names[0])==original,'mutation contaminated production cache'
    print('PASS flora: %d real models; %d-%d triangles; 3 real negative probes; immutable source restored; '
          'public metadata/refiner envelopes also checked'%(len(names),min(counts.values()),max(counts.values())))
    return 0


if __name__=='__main__':
    sys.exit(main())
