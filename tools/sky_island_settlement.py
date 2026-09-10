"""COMPAT: original settlement props, authored placement and low roadside planting.

Run with Blender before sky_island_navigation.py. The plan records actual model
bounds so the navigation generator and world exporter consume the same obstacles.
Only the planning entry imports Blender; the navigation reader uses standard Python.
"""

import argparse
import hashlib
import json
import math
import os
from pathlib import Path
import random
import sys

ROOT = Path(__file__).resolve().parents[1]
# 流水线可用环境变量把规划文件指到别处：先在草稿里把整条链跑通，再整体换进仓库。
PLAN_PATH = Path(os.environ.get('SKY_ISLAND_SETTLEMENT_PLAN') or ROOT / 'ArtSource/SkyIsland/settlement_layout.json')
METADATA_VERSION = 2


def load_obstacles():
    if not PLAN_PATH.is_file():
        return []
    plan = json.loads(PLAN_PATH.read_text(encoding='utf-8'))
    if plan['version'] != 1:
        raise ValueError('Unsupported settlement plan version')
    # 岛位换过参照系后，旧规划的摆件坐标全部失效：导航先不带摆件生成，再由本脚本按新岛重排。
    import sky_island_frame
    if plan.get('layoutFrame') != sky_island_frame.FRAME_VERSION:
        return []
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


def _mesh_counts(g):
    return {key: (len(data['v']), len(data['f'])) for key, data in g.GROUPS.items()}


def _mesh_delta(g, before, origin):
    parts = {}
    meshes = []
    for (group, material), data in g.GROUPS.items():
        first_vertex, first_face = before.get((group, material), (0, 0))
        vertices = data['v'][first_vertex:]
        faces = data['f'][first_face:]
        if not vertices and not faces:
            continue
        if not vertices or not faces or any(
                len(face) < 3 or any(i < first_vertex or i >= len(data['v']) for i in face)
                for face in faces):
            raise ValueError('Settlement geometry must append self-contained meshes')
        parts[(group, material)] = {'v': vertices}
        meshes.append({'name': 'VIS_'+group+'_'+material, 'material': 'Sky_'+material,
                       'vertices': len(vertices),
                       'triangles': sum(len(face)-2 for face in faces)})
    measured = bounds_of(parts)
    return {'bounds': measured, 'meshContributions': meshes,
            'vertices': sum(mesh['vertices'] for mesh in meshes),
            'triangles': sum(mesh['triangles'] for mesh in meshes),
            'measuredRadius': max(math.hypot(x-origin[0], z-origin[2])
                                  for data in parts.values() for x, y, z in data['v'])}


def _garden_radius(g):
    saved_groups, saved_current = g.GROUPS, g.CURRENT
    try:
        g.GROUPS = {}
        g.CURRENT = 'SettlementGardenProbe'
        g.garden_clump(0, 0, 0, 1.0)
        return _mesh_delta(g, {}, (0, 0, 0))['measuredRadius'] + .05
    finally:
        g.GROUPS, g.CURRENT = saved_groups, saved_current


def prop_side_candidates(obstacle, radius):
    """Sample the expanded actual AABB, leaving the model's -Z front open."""
    low_x, low_z, high_x, high_z = obstacle['bounds']
    gap = radius + 1.25  # PlantingSpace reserves radius + 1 around each obstacle.
    yaw = obstacle['modelYaw']
    front = (-math.sin(yaw), -math.cos(yaw))
    for fraction in (.25, .75, .5):
        x = low_x + (high_x-low_x)*fraction
        z = low_z + (high_z-low_z)*fraction
        edges = [((x, high_z+gap), (0, 1)), ((low_x-gap, z), (-1, 0)),
                 ((high_x+gap, z), (1, 0)), ((x, low_z-gap), (0, -1))]
        for point, normal in edges:
            if normal[0]*front[0] + normal[1]*front[1] <= .35:
                yield point


def _planting_record(g, before, sid, position, radius, category, kind, shrubs=False, prop_id=None):
    measured = _mesh_delta(g, before, position)
    if measured['measuredRadius'] > radius + 1e-6:
        raise ValueError('Plant geometry exceeds its reserved clearance: '+category+'/'+kind)
    result = {'island': sid, 'position': list(position), 'radius': radius,
              'category': category, 'kind': kind, 'hasShrubs': shrubs}
    result.update(measured)
    if prop_id is not None:
        result['propId'] = prop_id
    return result


def plan(g, layout):
    import sky_island_life_models as models
    from sky_island_dressing import PlantingSpace

    import sky_island_frame
    sky_island_frame.bind_layout(layout)
    islands = {island['id']: island for island in layout['islands']}
    layout['obstacles'] = [o for o in layout['obstacles'] if o['kind'] != 'life_prop']
    # Existing cultivated beds are low decoration, but their crops should not grow
    # through the new props. These reservations do not become navigation walls.
    # 与 generate_sky_island.farm_beds() 同一份田块，预留比田块四周各宽 1 米。
    for cx, cz, bed_w, bed_d in g.farm_beds(islands):
        layout['obstacles'].append({'id':'FarmBedReservation','island':'C',
            'kind':'planting_reservation','center':[cx,islands['C']['height'],cz],
            'size':[bed_w+2,.1,bed_d+2]})
    g.GROUPS.clear()
    g.PAVING_TRACKS.clear()
    g.make_paths(islands, layout['bridges'])
    g.GROUPS.clear()
    # Each site belongs to a small recognizable activity along an existing route.
    sites = authored_sites()
    records = []
    for ordinal, (sid, key, px, pz, degrees, scale) in enumerate(sites):
        # 摆件点按旧版岛位手写，先换算到当前岛位，再做就近避让。
        px, pz = sky_island_frame.relocate(sid, px, pz)
        island = islands[sid]
        parts = models.model_geometry(g, key)
        yaw = math.radians(degrees)
        local = bounds_of(parts, yaw=yaw, scale=scale)
        radius = max(math.hypot(x, z) for x in [local['min'][0], local['max'][0]]
                     for z in [local['min'][2], local['max'][2]])
        space = PlantingSpace(g, layout, island)
        candidates = [(px, pz)]
        # 布局 v2 主岛缩小后路网更密，就近空位可能在 22 米外；外扩到 60 米，并记下挪了多远供复核。
        for ring in range(1, 31):
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
                    'kind': 'life_prop', 'siteShiftMeters': round(math.dist(point, (px, pz)), 2),
                    'center': [(a+b)/2 for a, b in zip(low, high)],
                    'size': [b-a for a, b in zip(low, high)], 'yaw': 0,
                    'bounds': [low[0], low[2], high[0], high[2]],
                    'model': key, 'position': position, 'modelYaw': yaw, 'modelScale': scale}
        records.append(obstacle)
        layout['obstacles'].append(obstacle)
    return {'version': 1, 'classification': 'COMPAT', 'layoutFrame': sky_island_frame.FRAME_VERSION,
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
    expected = {'min': [c-s/2 for c, s in zip(obstacle['center'], obstacle['size'])],
                'max': [c+s/2 for c, s in zip(obstacle['center'], obstacle['size'])]}
    if any(abs(a-b) > .002 for side in ('min', 'max')
           for a, b in zip(expected[side], actual[side])):
        raise ValueError('Model changed after placement planning: '+obstacle['id'])
    before = _mesh_counts(g)
    models.stamp(g, obstacle['model'], obstacle['position'], obstacle['modelYaw'], obstacle['modelScale'])
    emitted = _mesh_delta(g, before, obstacle['position'])
    if any(abs(a-b) > .002 for side in ('min', 'max')
           for a, b in zip(actual[side], emitted['bounds'][side])):
        raise ValueError('Stamped model differs from current local geometry: '+obstacle['id'])
    return {'id': obstacle['id'], 'model': obstacle['model'], 'island': obstacle['island'],
            'position': list(obstacle['position']), 'modelYaw': obstacle['modelYaw'],
            'modelScale': obstacle['modelScale'], 'localBounds': bounds_of(parts),
            'bounds': emitted['bounds'], 'vertices': emitted['vertices'],
            'triangles': emitted['triangles'], 'meshContributions': emitted['meshContributions']}


def finish_gardens(g, layout, records):
    """Low planting follows path edges and groups around placed props, with measured clearance."""
    from sky_island_dressing import PlantingSpace
    from sky_island_nature_assets import stamp

    planned = json.loads(PLAN_PATH.read_text(encoding='utf-8'))
    current = {m['id']: m['position'] for m in layout['markers']}
    protected = {m['id']: m['position'] for m in planned['protectedMarkers']}
    if len(current) != len(layout['markers']) or current != protected:
        raise ValueError('Settlement displaced a gameplay marker')
    if (len({r['id'] for r in records}) != len(records)
            or {r['id'] for r in records} != {o['id'] for o in planned['obstacles']}):
        raise ValueError('Settlement plan was not consumed by navigation/world export')
    obstacles = {o['id']: o for o in layout['obstacles']}
    garden_radius = _garden_radius(g)
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
                    if not space.free(px, pz, .9) or any(
                            math.hypot(px-a, pz-b) < max(3.5, .9+r+.25)
                            for a, b, r in local_occupied):
                        continue
                    local_occupied.append((px,pz,.9))
                    before = _mesh_counts(g)
                    # Ground-level grey pebbles and folded leaves give a broken, soft verge.
                    if (index//3) % 3 == 1:
                        kind = 'pebble'
                        g.sphere((px,y+.16,pz),(.55,.22,.40),'RockLight',7,4,False)
                        edging += 1
                    else:
                        kind = 'leaf_clump'
                        stamp(g,'grass_leafs',(px,y+.02,pz),.62,rng.random()*math.tau)
                        tufts += 1
                    has_shrubs = rng.random() < .4
                    if has_shrubs:
                        for k in range(3):
                            angle=k*2.4
                            g.sphere((px+math.cos(angle)*.3,y+.28,pz+math.sin(angle)*.3),
                                     (.58,.40,.51),'Forest' if k==0 else 'Leaf',7,4,False)
                        shrubs += 1
                    planted.append(_planting_record(g, before, sid, (px,y,pz), .9,
                                                     'roadside', kind, has_shrubs))
        # Prop gardens frame small activities without obscuring usable front faces.
        for record in [r for r in records if r['island']==sid]:
            accepted = 0
            for px, pz in prop_side_candidates(obstacles[record['id']], garden_radius):
                if not space.free(px,pz,garden_radius) or any(
                        math.hypot(px-a,pz-b) < max(3.5, garden_radius+r+.25)
                        for a, b, r in local_occupied):
                    continue
                before = _mesh_counts(g)
                g.garden_clump(px,y,pz,1.0)
                planted.append(_planting_record(g, before, sid, (px,y,pz), garden_radius,
                                                'prop_side', 'garden_clump', True, record['id']))
                local_occupied.append((px,pz,garden_radius))
                accepted += 1
                if accepted == 3:
                    break
    prop_side = [p for p in planted if p['category']=='prop_side']
    covered = {p['propId'] for p in prop_side}
    return {'metadataVersion': METADATA_VERSION,
            'models': sorted({r['model'] for r in records}), 'instances':len(records),
            'placedModels':records,'roadsideShrubs':shrubs,'roadsidePebbles':edging,
            'roadsideLeafClumps':tufts,'roadsidePlantings':edging+tufts,
            'propSidePlantings':len(prop_side),'propSideShrubs':len(prop_side),
            'propSideCoveredProps':len(covered),
            'propsWithoutSidePlanting':sorted(r['id'] for r in records if r['id'] not in covered),
            'plantingPlacements':planted,
            'pavingTracks':[{'points':[list(point) for point in points],'width':width}
                            for points, width in g.PAVING_TRACKS],
            'evidence':'Generator mesh deltas and authored clearance; not FBX rendering or Unity physics proof',
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
