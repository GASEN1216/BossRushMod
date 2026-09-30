"""Independent L2 road geometry / connectivity regression, including real mutants.

PYTHONPATH=Build/sky-island-python-deps python tests/SkyIslandRoadPlacementPropertyTest.py
--mutations runs child processes against temporary broken baked JSON, then restores
and rechecks the exact bytes. No Blender, Unity, Build fixtures or planner imports
are needed for geometry; replay is tested separately under a Shapely import ban.
"""
from collections import defaultdict, deque
import argparse
import copy
import hashlib
import json
import math
from pathlib import Path
import subprocess
import sys
import tempfile

from shapely.geometry import Point, LineString, Polygon, box
from shapely.ops import unary_union

ROOT = Path(__file__).resolve().parents[1]
LAYOUT = ROOT / 'ArtSource/SkyIsland/layout.json'
ROADS = ROOT / 'ArtSource/SkyIsland/road_layout.json'
EXPECTED_ISLANDS = {'A', 'B', 'C', 'D', 'E', 'F', 'G', 'H', 'S1', 'S2', 'S3', 'S4'}
BUILDING_KINDS = {'dock_house', 'tea_house', 'house', 'mill', 'barn', 'temple',
                  'pavilion', 'post_house', 'workshop_shed', 'dome'}
TOL = 2e-7


def fingerprint(value):
    return hashlib.sha256(json.dumps(value, sort_keys=True, ensure_ascii=False,
                                    separators=(',', ':')).encode('utf-8')).hexdigest()


def require(condition, message):
    if not condition:
        raise AssertionError(message)


def geometry(layout, roads, verbose=True):
    """Never invoke the production Router, bake, _surface or triangulator."""
    islands = {i['id']: i for i in layout['islands']}
    require(set(islands) == EXPECTED_ISLANDS, 'must exercise all 12 author regions')
    obstacles = {o['id']: o for o in layout['obstacles']}
    actual = roads['actualRegisteredBounds']
    require(set(actual) == set(obstacles), 'actual envelope registry must cover all 88 entities')
    require(len(actual) == 88, 'full registered geometry probe is required')
    require(fingerprint(actual) == roads['sourceActualBoundsSha256'], 'actual envelope fingerprint stale')
    hard = defaultdict(list)
    envelopes = {}
    for identifier, o in obstacles.items():
        a, n = actual[identifier], o['bounds']
        require(len(a) == 4 and all(math.isfinite(v) for v in a) and a[0] < a[2] and a[1] < a[3],
                f'{identifier}: malformed actual bounds')
        # Explicit conservative AABB of both envelopes; do not call planner helper.
        bounds = [min(a[0], n[0]), min(a[1], n[1]), max(a[2], n[2]), max(a[3], n[3])]
        envelopes[identifier] = bounds
        hard[o['island']].append((identifier, box(*bounds)))
    tracks = {t['id']: t for t in roads['tracks']}
    require(len(tracks) == len(roads['tracks']), 'duplicate road identifiers')
    by_island, lines, expected_surfaces = defaultdict(list), {}, defaultdict(list)
    clearance = roads['policy']['entityClearance']
    require(clearance >= .2, 'clearance may not be removed to hide intersections')
    for identifier, t in tracks.items():
        sid, pts = t['island'], t['points']
        require(sid in islands and len(pts) >= 2, f'{identifier}: missing polyline')
        require(all(len(p) == 2 and all(math.isfinite(v) for v in p) for p in pts),
                f'{identifier}: invalid point')
        widths = t.get('segmentWidths', [t['width']] * (len(pts) - 1))
        require(len(widths) == len(pts) - 1 and all(3.8 <= w <= t['width'] for w in widths),
                f'{identifier}: width profile malformed or impassable')
        require(abs(t['height'] - islands[sid]['height'] - .045) < 1e-9,
                f'{identifier}: road crossing must share one elevation')
        domain = Polygon(islands[sid]['outline'])
        line = LineString(pts)
        lines[identifier] = line
        by_island[sid].append(t)
        require(line.difference(domain.buffer(TOL)).length < TOL, f'{identifier}: centerline outside island')
        require(line.is_simple or (identifier == 'legacy_29' and line.is_ring),
                f'{identifier}: folded / self-crossing route')
        swept = []
        for a, b, w in zip(pts, pts[1:], widths):
            require(TOL < math.dist(a, b) <= 1.250001, f'{identifier}: discontinuous or duplicate samples')
            segment = LineString([a, b])
            # Independent swept-disk distance, including full caps. Clipping must
            # not be used to disguise an obstacle collision.
            for oid, entity in hard[sid]:
                require(segment.distance(entity) >= w / 2 + clearance - TOL,
                        f'road_entity_overlap: {identifier} / {oid}, distance={segment.distance(entity):.6f}, width={w}')
            swept.append(segment.buffer(w / 2, quad_segs=32))
        expected_surfaces[sid].append(unary_union(swept).intersection(domain))

    require(set(by_island) == EXPECTED_ISLANDS, 'roads missing an island')
    source = roads['sourceControls']
    legacy = [t for t in tracks.values() if 'sourceIndex' in t]
    require(len(source) == len(legacy) == 30 and {t['sourceIndex'] for t in legacy} == set(range(30)),
            'all 30 original routes must survive')
    adjustment = {(a['track'], a['control']): a for a in roads['adjustments']}
    for t in legacy:
        old = source[t['sourceIndex']]
        require(t['island'] == old['island'] and t['width'] == old['width'], f'{t["id"]}: nominal route altered')
        resolved = t['resolvedControls']
        require(len(resolved) == len(old['points']), f'{t["id"]}: destination/control removed')
        for index, (original, point) in enumerate(zip(old['points'], resolved)):
            require(lines[t['id']].distance(Point(point)) < TOL,
                    f'{t["id"]}: original destination not served: control {index}')
            if math.dist(original, point) > TOL:
                note = adjustment.get((t['id'], index))
                require(note is not None and note['source'] == original and note['resolved'] == point,
                        f'{t["id"]}: undocumented destination move')
                require(math.dist(original, point) <= 15, f'{t["id"]}: unreasonable destination relocation')
                unsafe = (not Polygon(islands[t['island']]['outline']).buffer(-old['width'] / 2).covers(Point(original))
                          or any(Point(original).distance(o) < old['width'] / 2 + clearance for _, o in hard[t['island']]))
                require(unsafe, f'{t["id"]}: valid original destination moved unnecessarily')
        for end in (0, -1):
            require(math.dist(resolved[end], t['points'][end]) < TOL, f'{t["id"]}: route truncated')
    require(len(roads['destinations']) == 60, 'original route destinations missing')
    for destination in roads['destinations']:
        t = tracks[destination['track']]
        end = destination['end']
        require(destination['source'] == source[t['sourceIndex']]['points'][end]
                and math.dist(destination['point'], t['points'][end]) < TOL,
                f'{t["id"]}: destination metadata disagrees with real route')
    require(lines['legacy_29'].is_ring, 'mirror pool promenade must be a closed simple loop')
    require(Polygon(tracks['legacy_29']['points']).covers(box(*envelopes['F_MirrorPool'])),
            'mirror pool promenade must actually encircle the full pool')

    meshes = {m['island']: m for m in roads['meshes']}
    require(set(meshes) == EXPECTED_ISLANDS and len(roads['meshes']) == 12, 'merged layer required per island')
    surfaces = {}
    for sid, island in islands.items():
        mesh = meshes[sid]
        vertices, faces = mesh['vertices'], mesh['faces']
        require(vertices and faces, f'{sid}: empty paving mesh')
        require(len({tuple(v) for v in vertices}) == len(vertices), f'{sid}: vertices not welded')
        require(all(len(v) == 3 and all(math.isfinite(k) for k in v)
                    and abs(v[1] - island['height'] - .045) < 1e-9 for v in vertices),
                f'{sid}: finite coplanar merged layer required')
        triangles, total_area = [], 0.0
        for face in faces:
            require(len(face) == 3 and len(set(face)) == 3 and all(0 <= j < len(vertices) for j in face),
                    f'{sid}: malformed face')
            a, b, c = [vertices[j] for j in face]
            cross_y = (b[2] - a[2]) * (c[0] - a[0]) - (b[0] - a[0]) * (c[2] - a[2])
            require(cross_y > 1e-12, f'{sid}: degenerate or downward face')
            tri = Polygon([(v[0], v[2]) for v in (a, b, c)])
            total_area += tri.area
            triangles.append(tri)
        paved = unary_union(triangles)
        surfaces[sid] = paved
        require(paved.is_valid and paved.geom_type == 'Polygon', f'{sid}: paving disconnected / invalid')
        require(total_area - paved.area < 1e-5, f'{sid}: coplanar triangles overlap / z-fighting')
        require(abs(paved.area - mesh['area']) < 1e-5, f'{sid}: baked area does not match mesh')
        domain = Polygon(island['outline'])
        require(paved.difference(domain.buffer(TOL)).area < 1e-5, f'{sid}: curb outside island domain')
        for identifier, entity in hard[sid]:
            require(paved.distance(entity) >= clearance - TOL,
                    f'mesh_entity_overlap: {sid} / {identifier}')
        expected = unary_union(expected_surfaces[sid])
        # The independently oriented 128-sided disk has a known sagitta
        # <= 3.2*(1-cos(pi/128)) = .000964m. Add that analytic envelope
        # for inclusion; cap phase must not masquerade as missing road area.
        require(paved.difference(expected.buffer(.0011)).area < .0001,
                f'{sid}: exported mesh contains unrelated paving')
        require(expected.difference(paved).area / expected.area < .0015,
                f'{sid}: exported mesh missing required polyline strips')
        pedestrian = paved.buffer(-.6)
        require(pedestrian.geom_type == 'Polygon' and not pedestrian.is_empty,
                f'{sid}: connections pinch off a 1.2m pedestrian corridor')
        if verbose:
            print(f'PASS {sid}: tracks={len(by_island[sid])}, triangles={len(faces)}, area={paved.area:.2f}')

    registered_fronts = {o['id'] for o in obstacles.values() if o['kind'] in BUILDING_KINDS}
    require({f['obstacle'] for f in roads['frontages']} == registered_fronts, 'registered house fronts not served')
    for front in roads['frontages']:
        bounds = envelopes[front['obstacle']]
        obstacle = obstacles[front['obstacle']]
        centre = obstacle['center']
        island_centre = islands[front['island']]['center']
        vector = [island_centre[k] - centre[k] for k in (0, 2)]
        length = math.hypot(*vector)
        outward = [v / length for v in vector] if length > TOL else [0, -1]
        x, z = front['point']
        require(math.dist(front['outward'], outward) < TOL,
                f'{front["obstacle"]}: actual emitted model yaw ignored')
        require((x - centre[0]) * outward[0] + (z - centre[2]) * outward[1] > 0
                and Point(x, z).distance(box(*bounds)) >= front['width'] / 2 + clearance - TOL,
                f'{front["obstacle"]}: frontage is behind / inside roof or building')
        require(math.dist(front['point'], front['intended']) <= 8,
                f'{front["obstacle"]}: actual facade not reasonably served')
        require(surfaces[front['island']].buffer(TOL).covers(Point(x, z)),
                f'{front["obstacle"]}: frontage disconnected')

    portal_records = {(p['island'], p['bridge']): p for p in roads['portals']}
    require(len(portal_records) == len(roads['portals']) == 30, '30 precise bridge ends required')
    region_graph = defaultdict(set)
    for bridge in layout['bridges']:
        region_graph[bridge['from']].add(bridge['to'])
        region_graph[bridge['to']].add(bridge['from'])
        for sid, end, neighbor in ((bridge['from'], 0, 1), (bridge['to'], -1, -2)):
            point, adjacent = bridge['path'][end], bridge['path'][neighbor]
            p = [point[0], point[2]]
            q = [adjacent[0], adjacent[2]]
            length = math.dist(p, q)
            inward = [(p[i] - q[i]) / length for i in (0, 1)]
            record = portal_records[sid, bridge['id']]
            require(record['point'] == p and math.dist(record['inward'], inward) < TOL,
                    f'{sid}@{bridge["id"]}: bridge mouth moved / tangent altered')
            straight = record['straightLength']
            require(3 <= straight <= 8, f'{sid}@{bridge["id"]}: insufficient straight approach')
            candidates = []
            for t in by_island[sid]:
                for reverse in (False, True):
                    pts = list(reversed(t['points'])) if reverse else t['points']
                    if math.dist(pts[0], p) < TOL:
                        candidates.append((t, pts, reverse))
            require(candidates, f'bridge_disconnected: {sid}@{bridge["id"]}: exact route endpoint missing')
            nominal = 6.4 if len(sid) == 1 else 4.2
            cross_section = LineString([(p[0] + inward[1] * nominal / 2, p[1] - inward[0] * nominal / 2),
                                        (p[0] - inward[1] * nominal / 2, p[1] + inward[0] * nominal / 2)])
            require(cross_section.difference(surfaces[sid].buffer(TOL)).length < 1e-5,
                    f'bridge_disconnected: {sid}@{bridge["id"]}: bridge curb gap')
            aligned = False
            for t, pts, _ in candidates:
                distance = 0.0
                ok = True
                for a, b in zip(pts, pts[1:]):
                    if distance >= straight - TOL:
                        break
                    delta = [b[i] - a[i] for i in (0, 1)]
                    if abs(delta[0] * inward[1] - delta[1] * inward[0]) > TOL or sum(delta[i] * inward[i] for i in (0, 1)) <= 0:
                        ok = False
                        break
                    distance += math.dist(a, b)
                aligned |= ok and distance >= straight - TOL
            require(aligned, f'{sid}@{bridge["id"]}: baked approach violates fixed bridge tangent')
            inside = Point([p[i] + inward[i] * straight for i in (0, 1)])
            require(surfaces[sid].buffer(-.6).buffer(TOL).covers(inside),
                    f'{sid}@{bridge["id"]}: no pedestrian connection from bridge')
    visited, queue = set(), deque(['A'])
    while queue:
        sid = queue.popleft()
        if sid in visited:
            continue
        visited.add(sid)
        queue.extend(region_graph[sid] - visited)
    require(visited == EXPECTED_ISLANDS, 'bridges do not connect all twelve road networks')
    return {'islands': len(islands), 'tracks': len(tracks), 'portals': len(portal_records),
            'triangles': sum(len(m['faces']) for m in meshes.values()), 'entities': len(actual)}


def replay_without_shapely():
    code = r'''
import builtins, importlib.util, json
from types import SimpleNamespace
from pathlib import Path
root = Path.cwd()
original = builtins.__import__
def blocked(name, *args, **kwargs):
    if name.split('.')[0] in ('shapely', 'bpy'):
        raise AssertionError('Replay imported forbidden dependency: ' + name)
    return original(name, *args, **kwargs)
builtins.__import__ = blocked
spec = importlib.util.spec_from_file_location('road_replay', root/'tools/sky_island_road_layout.py')
m = importlib.util.module_from_spec(spec); spec.loader.exec_module(m)
layout = json.loads((root/'ArtSource/SkyIsland/layout.json').read_text(encoding='utf-8'))
calls = []
g = SimpleNamespace(CURRENT='sentinel', PAVING_TRACKS=[('stale', 99)],
                    addmesh=lambda mat, v, f: calls.append((g.CURRENT, mat, v, f)))
report = m.emit(g, layout)
assert g.CURRENT == 'sentinel' and len(calls) == 12 and g.PAVING_TRACKS
assert all(3.8 <= width <= 6.4 and len(points) >= 2 for points,width in g.PAVING_TRACKS)
assert not any(points == 'stale' for points,width in g.PAVING_TRACKS)
loaded = m.load(layout)
assert calls[0][2] == loaded['meshes'][0]['vertices']
count = len(g.PAVING_TRACKS); m.emit(g, layout)
assert len(g.PAVING_TRACKS) == count
layout['obstacles'][0]['bounds'][0] += .1
calls.clear()
try:
    m.emit(g, layout)
    raise AssertionError('stale layout was accepted')
except ValueError:
    pass
assert not calls and g.CURRENT == 'sentinel'
print('PASS dependency-free emit, exact replay, current restoration, stale-layout rejection')
'''
    result = subprocess.run([sys.executable, '-c', code], cwd=ROOT, capture_output=True, text=True)
    require(result.returncode == 0, 'dependency-free replay failed:\n' + result.stdout + result.stderr)
    print(result.stdout.strip())


def mutations(layout, roads):
    """Run genuine child failures; geometry precedes hash checking deliberately."""
    immutable = {p: hashlib.sha256(p.read_bytes()).hexdigest() for p in (LAYOUT, ROADS)}
    with tempfile.TemporaryDirectory(prefix='sky-road-negative-') as directory:
        path = Path(directory) / 'road_layout.json'
        original = ROADS.read_bytes()
        probes = []
        collision = copy.deepcopy(roads)
        t = next(t for t in collision['tracks'] if t['id'] == 'legacy_08')
        # Translate the whole simple sampled road 25cm into the real tea-house
        # roof envelope. Sampling and topology remain valid; collision must fail.
        for point in t['points']:
            point[1] += .25
        probes.append(('building_collision', collision, 'road_entity_overlap'))
        severed = copy.deepcopy(roads)
        t = next(t for t in severed['tracks'] if t['id'] == 'legacy_25')
        old_start = list(t['points'][0])
        t['points'][0][0] -= .4  # precise CS1 mouth now detached, footprint remains safe
        t['resolvedControls'][0] = list(t['points'][0])
        severed['adjustments'].append({'track': t['id'], 'control': 0,
                                      'source': old_start, 'resolved': list(t['points'][0]),
                                      'distance': .4, 'reason': 'negative_probe'})
        # Rebuild ONLY this mutant's S1 mesh consistently with its shortened
        # polyline. This defeats accidental detection via stale mesh/hash first.
        from shapely import constrained_delaunay_triangles, get_parts
        island = next(i for i in layout['islands'] if i['id'] == 'S1')
        polygon = LineString(t['points']).buffer(2.1, quad_segs=8).intersection(Polygon(island['outline']))
        mesh = next(m for m in severed['meshes'] if m['island'] == 'S1')
        vertices, faces = [], []
        for tri in get_parts(constrained_delaunay_triangles(polygon)):
            pts = list(tri.exterior.coords)[:-1]
            if tri.exterior.is_ccw:
                pts.reverse()
            face = []
            for x, z in pts:
                value = [x, island['height'] + .045, z]
                if value not in vertices:
                    vertices.append(value)
                face.append(vertices.index(value))
            faces.append(face)
        mesh.update(vertices=vertices, faces=faces, area=polygon.area)
        for d in severed['destinations']:
            if d['track'] == t['id'] and d['end'] == 0:
                d['point'] = list(t['points'][0])
        # Keep the declared source endpoint for independent exact-mouth validation.
        probes.append(('sever_bridge_mouth', severed, 'bridge_disconnected'))
        for name, broken, expected in probes:
            path.write_bytes(original)
            path.write_text(json.dumps(broken, ensure_ascii=False), encoding='utf-8')
            result = subprocess.run([sys.executable, str(Path(__file__).resolve()), '--roads', str(path),
                                     '--geometry-only'], cwd=ROOT, capture_output=True, text=True)
            require(result.returncode != 0 and expected in result.stdout + result.stderr,
                    f'mutant {name} did not fail at {expected}:\n{result.stdout}{result.stderr}')
            path.write_bytes(original)
            require(hashlib.sha256(path.read_bytes()).hexdigest() == immutable[ROADS], 'mutant not restored byte-for-byte')
            geometry(layout, json.loads(path.read_text(encoding='utf-8')), verbose=False)
            print(f'PASS negative mutation {name}: real subprocess FAIL at {expected}, bytes restored')
    require(all(hashlib.sha256(p.read_bytes()).hexdigest() == h for p, h in immutable.items()),
            'shared author inputs were modified by negative probes')


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--layout', type=Path, default=LAYOUT)
    parser.add_argument('--roads', type=Path, default=ROADS)
    parser.add_argument('--geometry-only', action='store_true')
    parser.add_argument('--mutations', action='store_true')
    args = parser.parse_args()
    layout = json.loads(args.layout.read_text(encoding='utf-8'))
    roads = json.loads(args.roads.read_text(encoding='utf-8'))
    report = geometry(layout, roads)
    if not args.geometry_only:
        require(roads['layoutHash'] == fingerprint(layout), 'baked layout canonical hash stale')
        require(roads['sourceLayoutSha256'] == hashlib.sha256(args.layout.read_bytes()).hexdigest(),
                'baked source layout file hash stale')
        require(roads['payloadHash'] == fingerprint({k: v for k, v in roads.items() if k != 'payloadHash'}),
                'baked payload corrupted')
        replay_without_shapely()
    if args.mutations:
        mutations(layout, roads)
    print('PASS SkyIslandRoadPlacementPropertyTest ' + json.dumps(report, sort_keys=True))


if __name__ == '__main__':
    main()
