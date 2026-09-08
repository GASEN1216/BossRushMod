"""COMPAT: original settlement props, authored placement and low roadside planting.

Run with Blender before sky_island_navigation.py. The plan records actual model
bounds so the navigation generator and world exporter consume the same obstacles.
Only the planning entry imports Blender; the navigation reader uses standard Python.
"""

import argparse
import hashlib
import json
import math
from pathlib import Path
import random
import sys

ROOT = Path(__file__).resolve().parents[1]
PLAN_PATH = ROOT / 'ArtSource/SkyIsland/settlement_layout.json'


def load_obstacles():
    if not PLAN_PATH.is_file():
        return []
    plan = json.loads(PLAN_PATH.read_text(encoding='utf-8'))
    if plan['version'] != 1:
        raise ValueError('Unsupported settlement plan version')
    return plan['obstacles']


def bounds_of(parts, position=(0, 0, 0), yaw=0, scale=1):
    c, s = math.cos(yaw), math.sin(yaw)
    points = [(position[0]+scale*(x*c+z*s), position[1]+scale*y,
               position[2]+scale*(-x*s+z*c))
              for data in parts.values() for x, y, z in data['v']]
    if not points or not all(math.isfinite(v) for p in points for v in p):
        raise ValueError('Invalid settlement model geometry')
    return {'min': [min(p[i] for p in points) for i in range(3)],
            'max': [max(p[i] for p in points) for i in range(3)]}


def plan(g, layout):
    import sky_island_life_models as models
    from sky_island_dressing import PlantingSpace

    islands = {island['id']: island for island in layout['islands']}
    layout['obstacles'] = [o for o in layout['obstacles'] if o['kind'] != 'life_prop']
    # Existing cultivated beds are low decoration, but their crops should not grow
    # through the new props. These reservations do not become navigation walls.
    for side in [-1,1]:
        for row in range(3):
            layout['obstacles'].append({'id':'FarmBedReservation','island':'C',
                'kind':'planting_reservation','center':[-230+side*32,16,-142+row*24],
                'size':[26,.1,18]})
    g.GROUPS.clear()
    g.PAVING_TRACKS.clear()
    g.make_paths(islands, layout['bridges'])
    g.GROUPS.clear()
    # Each site belongs to a small recognizable activity along an existing route.
    sites = authored_sites()
    records = []
    for ordinal, (sid, key, px, pz, degrees, scale) in enumerate(sites):
        island = islands[sid]
        parts = models.model_geometry(g, key)
        yaw = math.radians(degrees)
        local = bounds_of(parts, yaw=yaw, scale=scale)
        radius = max(math.hypot(x, z) for x in [local['min'][0], local['max'][0]]
                     for z in [local['min'][2], local['max'][2]])
        space = PlantingSpace(g, layout, island)
        candidates = [(px, pz)]
        for ring in range(1, 12):
            for step in range(16):
                angle = step*math.tau/16 + ordinal*.37
                candidates.append((px+ring*2*math.cos(angle), pz+ring*2*math.sin(angle)))
        point = next(((x, z) for x, z in candidates if space.free(x, z, radius+1.2)), None)
        if point is None:
            raise ValueError('No safe authored site for ' + sid + '/' + key)
        position = [round(point[0], 4), island['height']-local['min'][1], round(point[1], 4)]
        actual = bounds_of(parts, position, yaw, scale)
        low, high = actual['min'], actual['max']
        obstacle = {'id': sid+'_Life%02d_' % (ordinal+1)+key, 'island': sid,
                    'kind': 'life_prop', 'center': [(a+b)/2 for a, b in zip(low, high)],
                    'size': [b-a for a, b in zip(low, high)], 'yaw': 0,
                    'bounds': [low[0], low[2], high[0], high[2]],
                    'model': key, 'position': position, 'modelYaw': yaw, 'modelScale': scale}
        records.append(obstacle)
        layout['obstacles'].append(obstacle)
    return {'version': 1, 'classification': 'COMPAT',
            'source': 'tools/sky_island_life_models.py',
            'modelSourceSHA256': hashlib.sha256((ROOT/'tools/sky_island_life_models.py').read_bytes()).hexdigest(),
            'obstacles': records, 'protectedMarkers': layout['markers'],
            'policy': 'Actual transformed mesh bounds; retain roads, bridge mouths, gameplay markers and central arenas'}


def authored_sites():
    return [
        ('A','floatboat_cargo',-20,-336,-12,1.6),
        ('A','supply_handcart',-22,-295,24,1.35),
        ('B','produce_stall',-32,-153,-8,1.4),
        ('B','picnic_table',18,-177,-14,1.3),
        ('B','laundry_rack',-52,-87,8,1.3),
        ('B','duck_mailbox',31,-83,-15,1.2),
        ('B','tea_stove',-38,-108,-12,1.25),
        ('B','windmill_planter',48,-123,12,1.35),
        ('C','compost_planter',-300,-165,-10,1.5),
        ('C','supply_handcart',-181,-81,24,1.4),
        ('C','rain_barrel',-284,-63,-8,1.35),
        ('C','windmill_planter',-308,-81,14,1.5),
        ('D','camp_tent',-298,96,-20,1.5),
        ('D','firewood_rack',-200,130,8,1.4),
        ('D','tea_stove',-310,154,-15,1.25),
        ('E','wayfinding_sign',-42,148,-12,1.4),
        ('E','picnic_table',49,151,10,1.4),
        ('F','tea_stove',182,-88,-18,1.35),
        ('F','picnic_table',310,-48,16,1.4),
        ('G','tool_workbench',238,218,-12,1.6),
        ('G','floatboat_cargo',192,209,16,1.4),
        ('G','firewood_rack',309,130,-14,1.5),
        ('H','wayfinding_sign',-60,283,-20,1.4),
        ('H','picnic_table',46,341,12,1.5),
        ('S1','windmill_planter',-366,-219,-8,1.1),
        ('S2','duck_mailbox',-354,226,14,1.2),
        ('S3','camp_tent',371,-153,-18,1.1),
        ('S4','tool_workbench',368,318,12,1.1),
    ]


def place_model(g, obstacle):
    import sky_island_life_models as models

    parts = models.model_geometry(g, obstacle['model'])
    actual = bounds_of(parts, obstacle['position'], obstacle['modelYaw'], obstacle['modelScale'])
    expected = [obstacle['bounds'][0], obstacle['bounds'][1], obstacle['bounds'][2], obstacle['bounds'][3]]
    measured = [actual['min'][0], actual['min'][2], actual['max'][0], actual['max'][2]]
    if max(abs(a-b) for a, b in zip(expected, measured)) > .002:
        raise ValueError('Model changed after placement planning: '+obstacle['id'])
    models.stamp(g, obstacle['model'], obstacle['position'], obstacle['modelYaw'], obstacle['modelScale'])
    return {'id': obstacle['id'], 'model': obstacle['model'], 'island': obstacle['island'],
            'position': obstacle['position'], 'bounds': actual,
            'triangles': sum(len(face)-2 for data in parts.values() for face in data['f'])}


def finish_gardens(g, layout, records):
    """Low planting follows path edges and groups around placed props, with measured clearance."""
    from sky_island_dressing import PlantingSpace
    from sky_island_nature_assets import stamp

    rng = random.Random(170921)
    shrubs = 0
    edging = 0
    tufts = 0
    planted = []
    for island in layout['islands']:
        sid = island['id']
        g.CURRENT = sid
        y = island['height']
        space = PlantingSpace(g, layout, island)
        local_occupied = []
        for points, width in g.PAVING_TRACKS:
            for index in range(1, len(points)-1, 3):
                x, z = points[index]
                if abs(x-island['center'][0]) > island['size'][0]*.5 or abs(z-island['center'][2]) > island['size'][1]*.5:
                    continue
                if rng.random() > .62:
                    continue
                dx = points[index+1][0]-points[index-1][0]
                dz = points[index+1][1]-points[index-1][1]
                length = math.hypot(dx, dz)
                if length < .01:
                    continue
                nx, nz = dz/length, -dx/length
                for side in [-1, 1]:
                    offset = width/2+2.5+rng.random()*.9
                    px, pz = x+nx*side*offset, z+nz*side*offset
                    if not space.free(px, pz, .9) or any(math.hypot(px-a, pz-b) < 3.5 for a, b in local_occupied):
                        continue
                    local_occupied.append((px,pz))
                    planted.append({'island': sid, 'position': [px,y,pz], 'radius': .9})
                    # Ground-level grey pebbles and folded leaves give a broken, soft verge.
                    if (index//3) % 3 == 1:
                        g.sphere((px,y+.16,pz),(.55,.22,.40),'RockLight',7,4,False)
                        edging += 1
                    else:
                        stamp(g,'grass_leafs',(px,y+.02,pz),.62,rng.random()*math.tau)
                        tufts += 1
                    if rng.random() < .4:
                        for k in range(3):
                            angle=k*2.4
                            g.sphere((px+math.cos(angle)*.3,y+.28,pz+math.sin(angle)*.3),
                                     (.58,.40,.51),'Forest' if k==0 else 'Leaf',7,4,False)
                        shrubs += 1
        # Prop gardens frame small activities without obscuring usable front faces.
        for record in [r for r in records if r['island']==sid]:
            low, high = record['bounds']['min'], record['bounds']['max']
            for index, (px,pz) in enumerate([(low[0]-2,high[2]+1),(high[0]+2,high[2]+1),
                                           (low[0]-1,low[2]-2),(high[0]+1,low[2]-2)]):
                if not space.free(px,pz,1.2):
                    continue
                g.garden_clump(px,y,pz,1.0)
                planted.append({'island':sid,'position':[px,y,pz],'radius':1.2})
                shrubs += 1
    # The baseline marker coordinates must survive re-triangulation unchanged.
    planned = json.loads(PLAN_PATH.read_text(encoding='utf-8'))
    current = {m['id']:m['position'] for m in layout['markers']}
    if any(current.get(m['id']) != m['position'] for m in planned['protectedMarkers']):
        raise ValueError('Settlement displaced a gameplay marker')
    if len(records) != len(planned['obstacles']):
        raise ValueError('Settlement plan was not consumed by navigation/world export')
    return {'models': sorted({r['model'] for r in records}), 'instances':len(records),
            'placedModels':records,'roadsideShrubs':shrubs,'roadsidePebbles':edging,
            'roadsideLeafClumps':tufts,'plantingPlacements':planted,
            'protectedMarkerCount':len(current),'allPlannedModelsPlaced':True,
            'additionalRuntimeComponents':0}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--project', required=True)
    args = parser.parse_args(sys.argv[sys.argv.index('--')+1:])
    import generate_sky_island as g

    project = Path(args.project).resolve()
    layout = json.loads((project/'Assets/SkyIsland/sky_island_layout.json').read_text(encoding='utf-8-sig'))
    result = plan(g, layout)
    if len(result['obstacles']) < 24:
        raise ValueError('Incomplete original prop placement plan')
    PLAN_PATH.parent.mkdir(parents=True,exist_ok=True)
    PLAN_PATH.write_text(json.dumps(result,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
    print('SKY_ISLAND_SETTLEMENT_PLAN_PASS '+str(len(result['obstacles'])))


if __name__ == '__main__':
    sys.path.insert(0,str(Path(__file__).resolve().parent))
    main()
