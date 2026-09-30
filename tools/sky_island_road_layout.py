"""Sky-island offline road planner and dependency-free baked mesh replay.

Bake: PYTHONPATH=Build/sky-island-python-deps python tools/sky_island_road_layout.py
Blender: replace make_paths(...) with sky_island_road_layout.emit(g, layout).
Only baking imports Shapely; replay never re-interpolates the exported polylines.
Author inputs, obstacle positions, bridge paths and gameplay markers are immutable.
"""
from pathlib import Path
import argparse
import hashlib
import heapq
import json
import math

ROOT = Path(__file__).resolve().parents[1]
LAYOUT_PATH = ROOT / 'ArtSource/SkyIsland/layout.json'
BASELINE_PATH = ROOT / 'Build/sky-placement-20260930/baseline_roads.json'
ACTUAL_PATH = ROOT / 'Build/sky-placement-20260930/actual_registered_footprints.json'
BAKED_PATH = ROOT / 'ArtSource/SkyIsland/road_layout.json'
CLEARANCE = 0.2
PLANNING_SLACK = 0.02
ELEVATION = 0.045
FRONTAGE_KINDS = {'dock_house', 'tea_house', 'house', 'mill', 'barn',
                  'temple', 'pavilion', 'post_house', 'workshop_shed', 'dome'}


def digest(value):
    """Canonical semantic fingerprint: harmless JSON formatting is not a rebuild."""
    return hashlib.sha256(json.dumps(value, ensure_ascii=False, sort_keys=True,
                                    separators=(',', ':')).encode('utf-8')).hexdigest()


def planning_entities(layout, actual_bounds):
    """Fresh records; never mutate the canonical author layout."""
    for obstacle in layout['obstacles']:
        nominal = obstacle['bounds']
        measured = actual_bounds.get(obstacle['id'], nominal)
        yield {**obstacle, 'bounds': [min(nominal[0], measured[0]), min(nominal[1], measured[1]),
                                    max(nominal[2], measured[2]), max(nominal[3], measured[3])]}


def mouths(layout, sid):
    result = []
    for bridge in layout['bridges']:
        for island, end, adjacent in ((bridge['from'], 0, 1),
                                     (bridge['to'], -1, -2)):
            if island != sid:
                continue
            p, q = bridge['path'][end], bridge['path'][adjacent]
            length = math.hypot(p[0] - q[0], p[2] - q[2])
            direction = [(p[0] - q[0]) / length, (p[2] - q[2]) / length]
            result.append({'bridge': bridge['id'], 'point': [p[0], p[2]],
                           'inward': direction, 'straightLength': 8.0,
                           'bridgeWidth': bridge['width']})
    return result


class Router:
    """Visibility graph on conservatively inflated entities, not clipped old roads.

    Island-boundary erosion controls the whole paved width. Only bridge mouths
    receive a full-width approach corridor to the edge. Off-island destinations
    are projected inside, with the source point and displacement retained.
    """
    def __init__(self, layout, sid, width):
        from shapely.geometry import Polygon, LineString, box
        from shapely.ops import unary_union
        from shapely import get_parts
        self.sid, self.width = sid, width
        self.island = next(i for i in layout['islands'] if i['id'] == sid)
        self.domain = Polygon(self.island['outline'])
        self.entities = [o for o in layout['obstacles'] if o['island'] == sid]
        self.portals = mouths(layout, sid)
        radius = width / 2
        center_domain = self.domain.buffer(-radius - PLANNING_SLACK, join_style=2)
        approaches = []
        for portal in self.portals:
            p, d = portal['point'], portal['inward']
            q = [p[0] + d[0] * 10, p[1] + d[1] * 10]
            approaches.append(LineString([p, q]).buffer(radius + .04, cap_style=2))
        center_domain = unary_union([center_domain] + approaches).intersection(self.domain)
        inflated = unary_union([box(*o['bounds']).buffer(radius + CLEARANCE + PLANNING_SLACK,
                                                        join_style=2)
                                for o in self.entities])
        self.free = center_domain.difference(inflated)
        if self.free.is_empty:
            raise ValueError(f'{sid}: no free space at width {width}')
        self.tolerant = self.free.buffer(1e-8)
        for portal in self.portals:
            p, d = portal['point'], portal['inward']
            length = 8.0
            while length >= 3.0 and not self.sees(p, [p[k] + d[k] * length for k in (0, 1)]):
                length = round(length - .05, 4)
            if length < 3:
                raise ValueError(f'{sid}@{portal["bridge"]}: less than 3m straight approach')
            portal['straightLength'] = length
        # Vertices of every island component and every obstacle hole participate.
        self.nodes = []
        for poly in get_parts(self.free):
            if poly.geom_type != 'Polygon':
                continue
            for ring in [poly.exterior] + list(poly.interiors):
                self.nodes.extend(tuple(p) for p in list(ring.coords)[:-1])
        self.nodes = list(dict.fromkeys(self.nodes))
        self.adjacency = None

    def sees(self, a, b):
        from shapely.geometry import LineString
        return self.tolerant.covers(LineString([a, b]))

    def project(self, point):
        from shapely.geometry import Point
        from shapely.ops import nearest_points
        p = Point(point)
        if self.tolerant.covers(p):
            return list(point)
        q = nearest_points(self.free, p)[0]
        return [q.x, q.y]

    def graph(self):
        if self.adjacency is not None:
            return
        from shapely.geometry import LineString
        from shapely import covers
        self.adjacency = [[] for _ in self.nodes]
        for i, a in enumerate(self.nodes):
            js = range(i + 1, len(self.nodes))
            valid = covers(self.tolerant, [LineString([a, self.nodes[j]]) for j in js])
            for j, ok in zip(js, valid):
                if ok:
                    distance = math.dist(a, self.nodes[j])
                    self.adjacency[i].append((j, distance))
                    self.adjacency[j].append((i, distance))

    def route(self, start, end):
        from shapely.geometry import LineString
        from shapely import covers
        a, b = tuple(start), tuple(end)
        if math.dist(a, b) < 1e-8:
            return [list(a)]
        if self.sees(a, b):
            return [list(a), list(b)]
        self.graph()
        count = len(self.nodes)
        begin, finish = count, count + 1
        nodes = self.nodes + [a, b]
        edges = [list(e) for e in self.adjacency] + [[], []]
        for index in (begin, finish):
            valid = covers(self.tolerant, [LineString([nodes[index], p]) for p in self.nodes])
            for j, ok in enumerate(valid):
                if ok:
                    length = math.dist(nodes[index], nodes[j])
                    edges[index].append((j, length))
                    edges[j].append((index, length))
        distance, previous = {begin: 0.0}, {}
        queue = [(math.dist(a, b), 0.0, begin)]
        while queue:
            _, cost, index = heapq.heappop(queue)
            if cost > distance[index] + 1e-9:
                continue
            if index == finish:
                path = [index]
                while path[-1] != begin:
                    path.append(previous[path[-1]])
                return [list(nodes[j]) for j in reversed(path)]
            for neighbor, length in edges[index]:
                candidate = cost + length
                if candidate + 1e-9 < distance.get(neighbor, math.inf):
                    distance[neighbor], previous[neighbor] = candidate, index
                    heapq.heappush(queue, (candidate + math.dist(nodes[neighbor], b),
                                           candidate, neighbor))
        raise ValueError(f'{self.sid}: disconnected at width {self.width}: {a} -> {b}')


def _resample(points, step=1.25):
    """Piecewise linear, including every bend: no spline overshoot on replay."""
    result = [points[0]]
    for a, b in zip(points, points[1:]):
        count = max(1, math.ceil(math.dist(a, b) / step))
        result.extend([[a[k] + (b[k] - a[k]) * j / count for k in (0, 1)]
                       for j in range(1, count + 1)])
    return result


def _surface(track, domain):
    from shapely.geometry import LineString
    from shapely.ops import unary_union
    widths = track.get('segmentWidths', [track['width']] * (len(track['points']) - 1))
    pieces, begin = [], 0
    for i in range(1, len(widths) + 1):
        if i == len(widths) or widths[i] != widths[begin]:
            pieces.append(LineString(track['points'][begin:i + 1]).buffer(
                widths[begin] / 2, quad_segs=8, cap_style=1, join_style=1))
            begin = i
    return unary_union(pieces).intersection(domain)


def bake(layout, baseline, actual):
    from shapely.geometry import Point, LineString, Polygon, box
    from shapely.ops import unary_union, nearest_points
    from shapely import constrained_delaunay_triangles, get_parts
    tracks, adjustments, exceptions, destinations, frontages, meshes = [], [], [], [], [], []
    routers = {}
    actual_bounds = {o['id']: o['bounds'] for o in actual}
    if len(actual_bounds) != len(actual) or set(actual_bounds) != {o['id'] for o in layout['obstacles']}:
        raise ValueError('Actual probe must contain every registered obstacle exactly once')
    for o in actual:
        source = next(n for n in layout['obstacles'] if n['id'] == o['id'])
        if o['island'] != source['island'] or o['kind'] != source['kind']:
            raise ValueError(f'Actual probe identity mismatch: {o["id"]}')
    if len(baseline['controls']) != len(baseline['pavingTracks']):
        raise ValueError('Control / actual Catmull sample counts disagree')
    for control, sampled in zip(baseline['controls'], baseline['pavingTracks']):
        if (control['width'] != sampled['width'] or
                any(math.dist(control['points'][e], sampled['points'][e]) > .001 for e in (0, -1))):
            raise ValueError('Baseline actual samples do not match control endpoints / widths')
    import copy
    planning_layout = copy.deepcopy(layout)
    planning_layout['obstacles'] = list(planning_entities(layout, actual_bounds))

    def router(sid, width):
        key = (sid, width)
        if key not in routers:
            routers[key] = Router(planning_layout, sid, width)
        return routers[key]

    def append_track(sid, points, width, identifier, purpose, **extra):
        track = {'id': identifier, 'island': sid, 'width': width,
                 'height': next(i['height'] for i in layout['islands'] if i['id'] == sid) + ELEVATION,
                 'purpose': purpose, 'points': _resample(points), **extra}
        tracks.append(track)
        return track

    for index, control in enumerate(baseline['controls']):
        sid, width = control['island'], control['width']
        planning_width = 3.8 if index == 29 else 4.0 if index in (18, 20) else width
        r = router(sid, planning_width)
        anchors = []
        for j, original in enumerate(control['points']):
            intended = original
            if index == 6 and j == 2:
                # K1's old waypoint is inside the fixed garden tree. Resolve it
                # on the west face toward the next destination, avoiding a spur
                # that retraces the south tree corner and folds the same route.
                tree = next(o for o in r.entities if o['id'] == 'B_GardenTree03')
                intended = [tree['bounds'][0] - width / 2 - CLEARANCE - PLANNING_SLACK,
                            original[1]]
            if index == 29 and j in (1, 2, 3):
                # Original promenade controls sit on the pavilion posts. Its
                # north side belongs BETWEEN pool coping and the south post row.
                intended = [original[0], -44.91]
            resolved = r.project(intended)
            if math.dist(original, resolved) > 1e-5:
                hit = [o['id'] for o in r.entities
                       if box(*o['bounds']).buffer(width / 2 + CLEARANCE).covers(Point(original))]
                adjustments.append({'track': f'legacy_{index:02}', 'control': j,
                                    'source': original, 'resolved': resolved,
                                    'distance': math.dist(original, resolved),
                                    'obstacles': hit, 'reason': 'entity_clearance' if hit else 'island_edge'})
            anchors.append(resolved)
        points = [anchors[0]]
        for a, b in zip(anchors, anchors[1:]):
            try:
                points.extend(r.route(a, b)[1:])
            except ValueError:
                raise ValueError(f'legacy_{index:02} control {a}->{b} requires width review')
        track = append_track(sid, points, width, f'legacy_{index:02}',
                             'pool_promenade' if index == 29 else 'legacy_route', sourceIndex=index, resolvedControls=anchors)
        if planning_width < width:
            # Widen only segments whose entire swept area remains safe; individual
            # buffered segments include their caps, so width transitions cannot
            # leak into an entity or create disconnected slivers.
            hard = unary_union([box(*o['bounds']) for o in r.entities])
            samples = track['points']
            profile = []
            for a, b in zip(samples, samples[1:]):
                chosen = planning_width
                for candidate in [round(width - .2 * k, 2)
                                  for k in range(math.ceil((width - planning_width) / .2))]:
                    swept = LineString([a, b]).buffer(candidate / 2, quad_segs=8)
                    inside = swept.intersection(r.domain)
                    if inside.distance(hard) >= CLEARANCE + .005 and router(sid, candidate).sees(a, b):
                        chosen = candidate
                        break
                profile.append(chosen)
            track['segmentWidths'] = profile
            exceptions.append({'track': track['id'], 'island': sid,
                               'kind': 'local_width_reduction', 'nominalWidth': width,
                               'minimumWidth': min(profile),
                               'narrowLength': sum(math.dist(a, b) for a, b, w in zip(samples, samples[1:], profile) if w < width),
                               'reason': 'full actual envelope + nominal boxes leave insufficient room at temple shore / pool posts'})
        for end in (0, -1):
            destinations.append({'track': track['id'], 'end': end, 'island': sid,
                                 'source': control['points'][end], 'point': anchors[end]})

    # Production replace_obstacle rotates model front (-Z) toward island centre.
    # Connect that actual facade direction outside the complete conservative box;
    # no road enters roofs/porches or assumes that nominal yaw is the emitted yaw.
    for obstacle in planning_layout['obstacles']:
        if obstacle['kind'] not in FRONTAGE_KINDS:
            continue
        sid = obstacle['island']
        width = 4.2 if len(sid) > 1 else 6.4
        if obstacle['id'] == 'F_TempleHall':
            width = 4.0
            exceptions.append({'island': sid, 'obstacle': obstacle['id'],
                               'kind': 'narrow_frontage', 'nominalWidth': 6.4,
                               'actualWidth': width,
                               'reason': 'front court joins the constrained pool-side promenade'})
        r = router(sid, width)
        xmin, zmin, xmax, zmax = obstacle['bounds']
        cx, _, cz = obstacle['center']
        ix, _, iz = r.island['center']
        dx, dz = ix - cx, iz - cz
        length = math.hypot(dx, dz)
        outward = [dx / length, dz / length] if length > 1e-8 else [0.0, -1.0]
        radius = width / 2 + CLEARANCE + PLANNING_SLACK
        distances = []
        for axis, low, high in ((0, xmin, xmax), (1, zmin, zmax)):
            origin = (cx, cz)[axis]
            component = outward[axis]
            if abs(component) > 1e-9:
                limit = high + radius if component > 0 else low - radius
                distances.append((limit - origin) / component)
        # Ray exits the inflated AABB on the front-facing side.
        distance = min(distances)
        intended = [cx + outward[0] * distance, cz + outward[1] * distance]
        point = r.project(intended)
        if (sum((point[k] - (cx, cz)[k]) * outward[k] for k in (0, 1)) <= 0
                or math.dist(intended, point) > 8):
            raise ValueError(f'{obstacle["id"]}: actual front blocked: {intended} -> {point}')
        frontages.append({'obstacle': obstacle['id'], 'island': sid, 'point': point,
                          'width': width, 'side': 'actual_model_front',
                          'outward': outward, 'intended': intended,
                          'yaw': math.degrees(math.atan2(-outward[0], -outward[1])),
                          'orientationPolicy': 'production_model_negative_z_faces_island_centre'})
        own = [t for t in tracks if t['island'] == sid]
        candidates = []
        for t in own:
            q = nearest_points(LineString(t['points']), Point(point))[0]
            target = r.project([q.x, q.y])
            try:
                path = r.route(point, target)
            except ValueError:
                continue
            candidates.append((LineString(path).length if len(path) > 1 else 0, path, t))
        if not candidates:
            raise ValueError(f'{obstacle["id"]}: no facade connection')
        _, path, target_track = min(candidates, key=lambda c: c[0])
        if len(path) > 1 and math.dist(path[0], path[-1]) > .01:
            append_track(sid, path, width, 'front_' + obstacle['id'], 'building_front',
                         target=target_track['id'])

    # Bring disconnected routes onto the island's first route with genuine paths.
    # Physical intersections (not equal waypoint tokens) define connectivity.
    for island in layout['islands']:
        sid = island['id']
        r = router(sid, 4.2 if len(sid) > 1 else 6.4)
        own = [t for t in tracks if t['island'] == sid]
        connected = _surface(own[0], r.domain)
        pending = own[1:]
        serial = 0
        while pending:
            overlapping = [t for t in pending if connected.intersects(_surface(t, r.domain))]
            if overlapping:
                connected = unary_union([connected] + [_surface(t, r.domain) for t in overlapping])
                pending = [t for t in pending if t not in overlapping]
                continue
            candidates = []
            center_network = unary_union([LineString(t['points']) for t in tracks
                                          if t['island'] == sid and t not in pending])
            for t in pending:
                # Use a real road centre target, rather than just touching curbs.
                a, b = nearest_points(LineString(t['points']), center_network)
                a, b = r.project([a.x, a.y]), r.project([b.x, b.y])
                try:
                    path = r.route(a, b)
                except ValueError:
                    continue
                candidates.append((LineString(path).length, path, t))
            if not candidates:
                raise ValueError(f'{sid}: cannot reconnect routes')
            _, path, t = min(candidates, key=lambda c: c[0])
            serial += 1
            link = append_track(sid, path, r.width, f'join_{sid}_{serial}', 'network_connection')
            connected = unary_union([connected, _surface(link, r.domain), _surface(t, r.domain)])
            pending.remove(t)
        # All bridge mouths must be exact and remain straight for the recorded straight length.
        for portal in r.portals:
            p, d = portal['point'], portal['inward']
            inner = [p[k] + d[k] * portal['straightLength'] for k in (0, 1)]
            if not r.sees(p, inner):
                raise ValueError(f'{sid}@{portal["bridge"]}: blocked bridge approach')
            if not any(math.dist(p, t['points'][e]) < 1e-5 for t in own for e in (0, -1)):
                raise ValueError(f'{sid}@{portal["bridge"]}: missing legacy portal')
        for portal in r.portals:
            if portal['straightLength'] < 8:
                exceptions.append({'island': sid, 'bridge': portal['bridge'],
                                   'kind': 'shorter_straight_approach',
                                   'nominalLength': 8, 'actualLength': portal['straightLength'],
                                   'reason': 'fixed obstacle envelopes limit straight run; bridge mouth and tangent retained'})
        surfaces = [_surface(t, r.domain) for t in tracks if t['island'] == sid]
        merged = unary_union(surfaces)
        if merged.geom_type != 'Polygon':
            raise ValueError(f'{sid}: final road surface disconnected ({merged.geom_type})')
        triangles = list(get_parts(constrained_delaunay_triangles(merged)))
        vertices, faces, lookup = [], [], {}
        y = island['height'] + ELEVATION
        for triangle in triangles:
            coords = list(triangle.exterior.coords)[:-1]
            # Unity X/Y/Z upward winding (cross product +Y).
            if triangle.exterior.is_ccw:
                coords.reverse()
            face = []
            for x, z in coords:
                key = (round(x, 9), round(z, 9))
                if key not in lookup:
                    lookup[key] = len(vertices)
                    vertices.append([key[0], y, key[1]])
                face.append(lookup[key])
            a, b, c = [vertices[k] for k in face]
            cross_y = (b[2] - a[2]) * (c[0] - a[0]) - (b[0] - a[0]) * (c[2] - a[2])
            # GEOS may triangulate a zero-area sliver at coincident route joins.
            # Welding at nanometre precision must not export a degenerate face.
            if len(set(face)) < 3 or abs(cross_y) <= 1e-12:
                continue
            if cross_y < 0:
                raise ValueError(f'{sid}: invalid triangulation winding')
            faces.append(face)
        meshes.append({'island': sid, 'material': 'Limestone', 'vertices': vertices,
                       'faces': faces, 'area': merged.area})
    return {'schemaVersion': 1, 'coordinateSystem': 'Unity XYZ metres',
            'layoutHash': digest(layout), 'sourceLayoutSha256': digest(layout),
            'sourceLayoutHashPolicy': 'sha256_canonical_json_utf8_sorted_keys',
            'baselineHash': digest(baseline), 'sourceControls': baseline['controls'],
            'actualRegisteredBounds': actual_bounds,
            'sourceActualBoundsSha256': digest(actual_bounds),
            'policy': {'entityClearance': CLEARANCE, 'planningSlack': PLANNING_SLACK,
                       'elevation': ELEVATION, 'interpolation': 'piecewise_linear',
                       'obstacleEnvelope': 'aabb_enclosing_nominal_and_actual_full_visible_bounds',
                       'surface': 'island_clipped_round_buffer_union_constrained_triangulation',
                       'bridgeStraightLength': 8.0, 'fixedEntitiesMoved': False},
            'tracks': tracks, 'meshes': meshes,
            'portals': [{'island': i['id'], **p} for i in layout['islands']
                        for p in router(i['id'], 4.2 if len(i['id']) > 1 else 6.4).portals],
            'destinations': destinations, 'frontages': frontages,
            'adjustments': adjustments, 'exceptions': exceptions}


def load(layout, path=BAKED_PATH):
    """Strict stale-layout protection; requires only the Python standard library."""
    data = json.loads(Path(path).read_text(encoding='utf-8'))
    if data.get('schemaVersion') != 1 or data.get('layoutHash') != digest(layout):
        raise ValueError('Baked roads are stale: rebake against current layout.json')
    if digest({k: v for k, v in data.items() if k != 'payloadHash'}) != data.get('payloadHash'):
        raise ValueError('Baked road payload hash mismatch')
    return data


def emit(g, layout, path=BAKED_PATH):
    """Replay merged triangles and populate the generator's scatter exclusion tracks.

    Call once INSTEAD OF make_paths, before landscape/dressing road-aware placement.
    g.addmesh accepts Unity XYZ and upward winding; it handles Blender conversion.
    No smooth_path call: baked samples/joins are replayed without Catmull overshoot.
    """
    data = load(layout, path)
    previous = g.CURRENT
    try:
        for mesh in data['meshes']:
            g.CURRENT = mesh['island'] + '_Paving'
            g.addmesh(mesh['material'], mesh['vertices'], mesh['faces'])
        g.PAVING_TRACKS.clear()
        for track in data['tracks']:
            points = track['points']
            widths = track.get('segmentWidths', [track['width']] * (len(points) - 1))
            begin = 0
            for i in range(1, len(widths) + 1):
                if i == len(widths) or widths[i] != widths[begin]:
                    g.PAVING_TRACKS.append(([(p[0], p[1]) for p in points[begin:i + 1]], widths[begin]))
                    begin = i
    finally:
        g.CURRENT = previous
    return {'tracks': len(data['tracks']), 'meshes': len(data['meshes']),
            'layoutHash': data['layoutHash'], 'payloadHash': data['payloadHash']}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--layout', type=Path, default=LAYOUT_PATH)
    parser.add_argument('--baseline', type=Path, default=BASELINE_PATH)
    parser.add_argument('--actual', type=Path, default=ACTUAL_PATH)
    parser.add_argument('--output', type=Path, default=BAKED_PATH)
    args = parser.parse_args()
    layout = json.loads(args.layout.read_text(encoding='utf-8'))
    baseline = json.loads(args.baseline.read_text(encoding='utf-8'))
    actual = json.loads(args.actual.read_text(encoding='utf-8'))
    data = bake(layout, baseline, actual)
    data['sourceLayoutSha256'] = hashlib.sha256(args.layout.read_bytes()).hexdigest()
    data['sourceLayoutHashPolicy'] = 'sha256_file_bytes; layoutHash uses canonical JSON'
    data['sourceActualProbeSha256'] = hashlib.sha256(args.actual.read_bytes()).hexdigest()
    data['sourceReference'] = {'baseline': str(args.baseline.relative_to(ROOT)) if args.baseline.is_relative_to(ROOT) else args.baseline.name,
                               'actualProbe': str(args.actual.relative_to(ROOT)) if args.actual.is_relative_to(ROOT) else args.actual.name,
                               'runtimeNeedsBuildInputs': False}
    # Explain exact geometry evidence without an entity / porch overlap exemption.
    entities = {o['id']: o['bounds'] for o in planning_entities(layout, data['actualRegisteredBounds'])}
    pool, column = entities['F_MirrorPool'], entities['F_PavilionSupport08']
    data['clearanceEvidence'] = [{'island': 'F', 'kind': 'pool_post_corridor',
                                'entities': ['F_MirrorPool', 'F_PavilionSupport08'],
                                'gap': column[1] - pool[3], 'maximumWidthAtClearance': column[1] - pool[3] - 2 * CLEARANCE,
                                'selectedMinimumWidth': 3.8, 'entityOverlapExceptions': []}]
    data['payloadHash'] = digest(data)
    args.output.write_text(json.dumps(data, ensure_ascii=False, indent=2) + '\n', encoding='utf-8')
    print(json.dumps({'output': str(args.output), 'tracks': len(data['tracks']),
                      'triangles': sum(len(m['faces']) for m in data['meshes']),
                      'adjustments': len(data['adjustments']), 'exceptions': data['exceptions']},
                     ensure_ascii=False))


if __name__ == '__main__':
    main()
