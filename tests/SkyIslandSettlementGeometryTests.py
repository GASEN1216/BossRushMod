"""Validate the shipped art outputs against gameplay geometry, not generator tokens.

This is offline geometry evidence. Unity physics and game camera verification are
separate; a JSON report cannot prove either of those runtime properties.
"""

import copy
import hashlib
import json
import math
import sys
from collections import Counter
from types import SimpleNamespace
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
ART = ROOT/'ArtSource/SkyIsland'
sys.path.insert(0, str(ROOT/'tools'))
from sky_island_dressing import PlantingSpace


def close(a, b):
    return len(a)==len(b) and all(abs(x-y)<.003 for x,y in zip(a,b))


def check(plan, layout, geometry, dressing, kit):
    expected = {o['id']:o for o in plan['obstacles']}
    placed = dressing['settlement']['placedModels']
    actual = {p['id']:p for p in placed}
    colliders = {c['name']:c for c in geometry['collisionBoxes']}
    obstacles = {o['id']:o for o in layout['obstacles']}
    assert len(expected)>=24 and len({o['model'] for o in expected.values()})>=12, 'Original model coverage is incomplete'
    assert set(actual)==set(expected) and len(actual)==len(placed), 'Missing or duplicate placed model'
    markers = {m['id']:m['position'] for m in layout['markers']}
    exported = {m['name']:m['position'] for m in geometry['markers']}
    assert len(markers)==len(plan['protectedMarkers']), 'Gameplay marker count changed'
    for marker in plan['protectedMarkers']:
        assert close(markers[marker['id']],marker['position']), 'Gameplay marker moved: '+marker['id']
        assert marker['id'] in exported and close(exported[marker['id']],marker['position']), 'Exported marker moved: '+marker['id']
    kit_models = {m['name'].removeprefix('SkyIsland_'):m for m in kit['models']}
    contributions = Counter()
    for key, wanted in expected.items():
        assert key in obstacles and key in colliders, 'Missing solid/navigation obstacle: '+key
        collider=colliders[key]
        obstacle=obstacles[key]
        placement=actual[key]
        assert placement['model']==wanted['model'] and placement['island']==wanted['island'], 'Wrong model at '+key
        assert close(placement['position'],wanted['position']), 'Wrong model origin at '+key
        assert close(collider['center'],obstacle['center']) and close(collider['size'],obstacle['size']), 'Physics/nav mismatch: '+key
        low=placement['bounds']['min']; high=placement['bounds']['max']
        assert all(math.isfinite(v) for v in low+high) and all(b>a for a,b in zip(low,high)), 'Degenerate model: '+key
        assert close([(a+b)/2 for a,b in zip(low,high)],collider['center']), 'Visible model outside collider: '+key
        assert close([b-a for a,b in zip(low,high)],collider['size']), 'Visible model size differs: '+key
        assert close([low[0],low[2],high[0],high[2]],obstacle['bounds']), 'Navigation hole differs: '+key
        assert 0 < placement['triangles'] < 12000, 'Missing or excessive prop geometry: '+key
        model = kit_models[placement['model']]
        assert placement['triangles']==model['triangles'], 'Kit/world geometry differs: '+key
        used = Counter()
        for mesh in placement['meshContributions']:
            assert mesh['name'] in geometry['visualMeshes'], 'Exported visual mesh missing: '+mesh['name']
            used[mesh['material']] += mesh['triangles']
            contributions[mesh['name']] += mesh['triangles']
            assert geometry['materials'][mesh['material']]==kit['materials'][mesh['material']], 'Kit/world material differs'
        assert used==Counter({m['material']:m['triangles'] for m in model['meshes']}), 'Kit/world material mesh differs'
        height=next(i['height'] for i in layout['islands'] if i['id']==wanted['island'])
        assert abs(low[1]-height)<.003, 'Floating or sunken model: '+key
    settlement = dressing['settlement']
    tracks = SimpleNamespace(PAVING_TRACKS=[(p['points'],p['width']) for p in settlement['pavingTracks']])
    spaces = {i['id']:PlantingSpace(tracks,layout,i) for i in layout['islands']}
    side_props = set()
    for plant in settlement['plantingPlacements']:
        x,y,z = plant['position']; radius=plant['radius']
        assert math.isfinite(radius) and radius>0, 'Invalid planting radius'
        assert spaces[plant['island']].free(x,z,radius), 'Planting blocks protected space'
        assert plant['measuredRadius']<=radius+.003, 'Plant geometry exceeds clearance'
        if plant['category']=='prop_side':
            assert plant['propId'] in actual, 'Unknown planted prop'
            side_props.add(plant['propId'])
        for mesh in plant['meshContributions']:
            assert mesh['name'] in geometry['visualMeshes'], 'Plant mesh missing'
            contributions[mesh['name']] += mesh['triangles']
    assert side_props==set(actual), 'Prop-side planting silently skipped'
    for name,triangles in contributions.items():
        assert geometry['visualMeshes'][name]['triangles']>=triangles, 'Exported mesh lost settlement geometry'
    return {'status':'PASS','modelTypes':len({p['model'] for p in placed}),
            'modelInstances':len(placed),'protectedMarkers':len(markers),
            'propTriangles':sum(p['triangles'] for p in placed)}


def main():
    read=lambda path:json.loads(path.read_text(encoding='utf-8-sig'))
    plan=read(ART/'settlement_layout.json')
    layout=read(ART/'layout.json')
    geometry=read(ART/'Validation/sky_island_geometry.json')
    dressing=read(ART/'Validation/sky_island_dressing.json')
    kit=read(ART/'Validation/sky_island_model_kit.json')
    source=ROOT/plan['source']
    assert hashlib.sha256(source.read_bytes()).hexdigest()==plan['modelSourceSHA256'], 'Models changed after planning'
    report=check(plan,layout,geometry,dressing,kit)
    mutations=[]
    dropped=copy.deepcopy(dressing); dropped['settlement']['placedModels'].pop()
    mutations.append((plan,layout,geometry,dropped))
    missing=copy.deepcopy(geometry)
    key=plan['obstacles'][0]['id']
    missing['collisionBoxes']=[c for c in missing['collisionBoxes'] if c['name']!=key]
    mutations.append((plan,layout,missing,dressing))
    sunk=copy.deepcopy(dressing)
    sunk['settlement']['placedModels'][0]['bounds']['min'][1]-=2
    mutations.append((plan,layout,geometry,sunk))
    moved=copy.deepcopy(layout)
    moved['markers'][0]['position'][0]+=3
    mutations.append((plan,moved,geometry,dressing))
    empty=copy.deepcopy(geometry); empty['visualMeshes']={}
    mutations.append((plan,layout,empty,dressing))
    shifted=copy.deepcopy(geometry); shifted['markers'][0]['position'][0]+=20
    mutations.append((plan,layout,shifted,dressing))
    planted=copy.deepcopy(dressing)
    resident=next(m for m in layout['markers'] if m['id'].startswith('POI_B'))
    planted['settlement']['plantingPlacements'][0].update(position=resident['position'],island='B',radius=10)
    mutations.append((plan,layout,geometry,planted))
    tiled=copy.deepcopy(geometry); tiled['materials']['Sky_PaintTeal']['texture']='Textures/roof_tiles.png'
    mutations.append((plan,layout,tiled,dressing))
    for args in mutations:
        try:
            check(*args,kit)
        except AssertionError:
            continue
        raise AssertionError('Corrupted art output was accepted')
    report['rejectedCorruptions']=len(mutations)
    print('SKY_ISLAND_SETTLEMENT_GEOMETRY_PASS '+json.dumps(report))


if __name__=='__main__':
    main()
