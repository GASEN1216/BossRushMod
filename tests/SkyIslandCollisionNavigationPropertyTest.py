"""碰撞导航 sidecar 独立属性回归：标准库几何覆盖、拓扑及生产门控。
不导入求解器，不修改资源；反向探针只修改 deepcopy 并核对源字节 SHA。
实体指纹执行生产导出与 apply_navigation，用内存网格验证硬物集合和桥面失效。
"""
from collections import defaultdict, deque
import argparse
import copy
import hashlib
import json
import math
import os
import importlib
from pathlib import Path
import string
import sys
from types import SimpleNamespace
from unittest.mock import Mock, patch

sys.dont_write_bytecode = True
import SkyIslandGateNavigationPropertyTest as gate_test
from SkyIslandNavigationPropertyTest import area, bounds, cross, height, inside, overlaps, xz

ROOT = Path(__file__).resolve().parents[1]
LAYOUT = ROOT / 'ArtSource/SkyIsland/layout.json'
SIDECAR = ROOT / 'ArtSource/SkyIsland/collision_navigation.json'
REQUIRED_KINDS = {'spawn', 'enemy_spawn', 'extraction', 'extraction_alias', 'relay'}
# 60 micrometres, below the 1 mm Unity/A* coordinate quantization.
EPS = 0.00006
APPROACH = 2.4

def require(condition, code, detail):
    if not condition:
        raise AssertionError(code + ': ' + str(detail))

def clip_triangle(subject, clip):
    """Convex Sutherland-Hodgman intersection, independent of the solver."""
    polygon = list(subject)
    sign = 1 if cross(*clip) > 0 else -1
    for a, b in zip(clip, clip[1:] + clip[:1]):
        if not polygon:
            break
        result = []
        for p, q in zip(polygon, polygon[1:] + polygon[:1]):
            dp, dq = sign * cross(a, b, p), sign * cross(a, b, q)
            pin, qin = dp >= 0, dq >= 0
            if pin:
                result.append(p)
            if pin != qin:
                t = dp / (dp - dq)
                result.append((p[0] + t * (q[0] - p[0]), p[1] + t * (q[1] - p[1])))
        polygon = result
    return polygon

def topology(sidecar, layout):
    nav = sidecar['navigation']
    vertices, faces, regions = nav['vertices'], nav['triangles'], nav['triangleRegions']
    require(0 < len(vertices) < 4095, 'BUDGET', len(vertices))
    require(faces and len(faces) == len(regions), 'TABLE_LENGTH', (len(faces), len(regions)))
    islands = {i['id'] for i in layout['islands']}
    bridges = {b['id'] for b in layout['bridges']}
    require(len(islands) == 12 and len(bridges) == 15, 'REGION_COUNT', (islands, bridges))
    require(set(regions) == islands | bridges, 'REGIONS', set(regions))
    for i, p in enumerate(vertices):
        require(len(p) == 3 and all(type(v) in (int, float) and math.isfinite(v) for v in p), 'VERTEX', (i, p))
    edges, rows, used, seen_faces = defaultdict(list), [], set(), set()
    for index, (face, region) in enumerate(zip(faces, regions)):
        require(len(face) == 3 and len(set(face)) == 3
                and all(type(v) is int and 0 <= v < len(vertices) for v in face), 'TRIANGLE', (index, face))
        signature = tuple(sorted(face))
        require(signature not in seen_faces, 'DUPLICATE_FACE', index)
        seen_faces.add(signature)
        points = [vertices[i] for i in face]
        poly = [xz(p) for p in points]
        require(cross(*poly) < -1e-8, 'DEGENERATE_OR_WINDING', (index, region, points))
        quantized = [(round(p[0], 3), round(p[2], 3)) for p in points]
        require(cross(*quantized) < -1e-8, 'QUANTIZED_DEGENERATE', (index, region, points))
        rows.append((index, region, points, poly, bounds(poly)))
        used.update(face)
        for a, b in zip(face, face[1:] + face[:1]):
            edges[tuple(sorted((a, b)))].append(index)
    require(len(used) == len(vertices), 'UNUSED_VERTICES', len(vertices) - len(used))
    links, interfaces = defaultdict(set), set()
    for edge, owners in edges.items():
        require(len(owners) <= 2, 'NON_MANIFOLD', (edge, owners))
        if len(owners) == 2:
            a, b = owners
            links[a].add(b); links[b].add(a)
            interfaces.add(frozenset((regions[a], regions[b])))
    visited, pending = {0}, deque([0])
    while pending:
        for n in links[pending.popleft()]:
            if n not in visited:
                visited.add(n); pending.append(n)
    require(len(visited) == len(faces), 'CONNECTED',
            {'reached': len(visited), 'total': len(faces), 'firstMissing': next((i for i in range(len(faces)) if i not in visited), None)})
    require(sidecar.get('connectedTriangles') == len(visited), 'CONNECTED_REPORT', sidecar.get('connectedTriangles'))
    for bridge in layout['bridges']:
        for end in (bridge['from'], bridge['to']):
            require(frozenset((bridge['id'], end)) in interfaces, 'BRIDGE_INTERFACE', (bridge['id'], end))
    return rows

def verify_support(rows, layout):
    original = layout['navigation']
    by_region = defaultdict(list)
    for face, region in zip(original['triangles'], original['triangleRegions']):
        pts = [original['vertices'][v] for v in face]
        poly = [xz(p) for p in pts]
        by_region[region].append((pts, poly, bounds(poly)))
    physical_bridges = {b['id']: b['surfaceTriangles'] for b in layout['bridges']}
    total_area = 0.0
    for index, region, points, poly, box in rows:
        expected_area, covered = area(poly), 0.0
        for source_pts, source_poly, source_box in by_region[region]:
            if not overlaps(box, source_box, EPS):
                continue
            piece = clip_triangle(poly, source_poly)
            piece_area = area(piece)
            if piece_area <= 1e-12:
                continue
            covered += piece_area
            for p in piece:
                error = abs(height(p, points) - height(p, source_pts))
                require(error <= EPS, 'HEIGHT', {'face': index, 'region': region, 'xz': p, 'error': error})
        perimeter = sum(math.dist(a, b) for a, b in zip(poly, poly[1:] + poly[:1]))
        require(abs(covered - expected_area) <= max(1e-7, perimeter * EPS), 'DESIGN_DOMAIN',
                {'face': index, 'region': region, 'triangle': points, 'area': expected_area, 'covered': covered})
        if region in physical_bridges:
            for p in points:
                matching = [tri for tri in physical_bridges[region] if inside(xz(p), [xz(q) for q in tri])]
                require(matching and any(abs(p[1] - height(xz(p), tri)) <= EPS for tri in matching),
                        'BRIDGE_HEIGHT', {'face': index, 'bridge': region, 'point': p})
        total_area += expected_area
    return total_area

def marker_matches(marker, rows):
    p = marker['position']
    return [row for row in rows if row[1] == marker['island'] and inside(xz(p), row[3])
            and abs(p[1] - height(xz(p), row[2])) <= EPS]

def verify_required_markers(rows, layout):
    required = [m for m in layout['markers'] if m['kind'] in REQUIRED_KINDS]
    require(any(m['id'] == 'PlayerSpawn' for m in required), 'SPAWN', 'PlayerSpawn')
    for marker in required:
        require(marker_matches(marker, rows), 'REQUIRED_MARKER',
                {'id': marker['id'], 'kind': marker['kind'], 'point': marker['position']})
    return len(required)

def closest_on_segment(p, a, b):
    dx, dz = b[0] - a[0], b[1] - a[1]
    denominator = dx * dx + dz * dz
    t = max(0.0, min(1.0, ((p[0] - a[0]) * dx + (p[1] - a[1]) * dz) / denominator)) if denominator else 0.0
    return (a[0] + t * dx, a[1] + t * dz)

def gate_layout(sidecar, layout, rows):
    # Legacy Topology requires every marker on a triangle. Non-spawn props may occupy
    # their center: expose explicitly reported same-region approaches <= 2.4 m.
    result = copy.deepcopy(layout)
    result['navigation'] = copy.deepcopy(sidecar['navigation'])
    approaches, approach_failures = [], []
    for marker in result['markers']:
        if marker['kind'] in REQUIRED_KINDS:
            continue
        target = xz(marker['position'])
        candidates = []
        for _, region, points, poly, _ in rows:
            if region != marker['island']:
                continue
            q = target if inside(target, poly) else min(
                (closest_on_segment(target, a, b) for a, b in zip(poly, poly[1:] + poly[:1])),
                key=lambda p: math.dist(p, target))
            # Move 2 mm into this triangle so the existing gate test's Int3 rounding
            # cannot move an edge-only witness to the wrong side of the boundary.
            center = (sum(p[0] for p in poly) / 3, sum(p[1] for p in poly) / 3)
            length = math.dist(q, center)
            t = min(1.0, 0.002 / length) if length else 0.0
            q = (q[0] + t * (center[0] - q[0]), q[1] + t * (center[1] - q[1]))
            distance = math.dist(q, target)
            if distance <= APPROACH and abs(height(q, points) - marker['position'][1]) <= EPS:
                candidates.append((distance, q, height(q, points)))
        if not candidates:
            approach_failures.append({'kind': 'MARKER_APPROACH', 'id': marker['id'],
                                      'point': marker['position'], 'maximum': APPROACH})
            continue
        distance, q, y = min(candidates)
        marker['position'] = [q[0], y, q[1]]
        if distance > EPS:
            approaches.append({'id': marker['id'], 'distance': round(distance, 6), 'point': marker['position']})
    # verify() observes these marker identities; unrelated props are checked above
    # and keep their failures, without preventing the independent 32 gate scenarios.
    gate_markers = {'PlayerSpawn', 'Search_S2', 'Search_S3'} | {'POI_' + i['id'] for i in layout['islands']}
    result['markers'] = [m for m in result['markers'] if m['kind'] in REQUIRED_KINDS or m['id'] in gate_markers]
    return result, approaches, approach_failures

def check(sidecar, layout, gates):
    rows = topology(sidecar, layout)
    total_area = verify_support(rows, layout)
    marker_count = verify_required_markers(rows, layout)
    adapted, approaches, approach_failures = gate_layout(sidecar, layout, rows)
    gate_report = gate_test.verify(adapted, gates)
    return {'vertices': len(sidecar['navigation']['vertices']), 'triangles': len(rows),
            'connectedComponents': 1, 'islands': 12, 'bridges': 15, 'areaXZ': round(total_area, 6),
            'requiredMarkers': marker_count, 'gateCombinations': 32, 'gates': gate_report,
            'propApproaches': approaches, 'failures': approach_failures}, adapted

def expect_red(label, action, expected):
    try:
        action()
    except AssertionError as error:
        require(expected in str(error), 'WRONG_NEGATIVE_FAILURE', (label, str(error), expected))
        return label
    raise AssertionError('NEGATIVE_DID_NOT_FAIL: ' + label)

class IdentityMatrix:
    def __matmul__(self, point):
        return point


def memory_mesh(name, materials):
    # Blender coordinates: X/east, Y/north, Z/up. One real body-height face.
    points = [(0.0, 0.0, 0.3), (1.0, 0.0, 0.3), (0.0, 1.0, 1.2)]
    mesh = SimpleNamespace(
        vertices=[SimpleNamespace(co=SimpleNamespace(x=x, y=y, z=z)) for x, y, z in points],
        loop_triangles=[SimpleNamespace(vertices=(0, 1, 2))],
        materials=[SimpleNamespace(name=m) if m is not None else None for m in materials],
        calc_loop_triangles=lambda: None)
    return SimpleNamespace(name=name, type='MESH', data=mesh, matrix_world=IdentityMatrix())


def verify_fingerprints(project):
    """Execute production export/apply; replace only bpy and file writes/inputs in memory."""
    sys.path.insert(0, str(ROOT / 'tools')) if str(ROOT / 'tools') not in sys.path else None
    import sky_island_collision_navigation as production
    policy = production.read_collision_policy(project)
    require('Leaf' in policy['soft_materials'] and 'Rock' not in policy['soft_materials'],
            'POLICY_FIXTURE', 'current author Leaf/Rock classification')
    island, bridge, soft = 'VIS_A_Rock', 'VIS_Bridge_AB_Wood', 'VIS_A_Leaf'
    objects = {island: memory_mesh(island, ['Sky_Rock (Instance)']),
               bridge: memory_mesh(bridge, ['Sky_Wood']), soft: memory_mesh(soft, ['Sky_Leaf'])}
    # Exclusions are checked before material validation, as in the C# builder.
    for name in ('VIS_Ground_A', 'VIS_A_Flora_BrassLight', 'VIS_A_Paving_Limestone',
                 'VIS_A_GroundDetail_Wood', 'VIS_A_Geology'):
        objects[name] = memory_mesh(name, [])
    layout = {'islands': [{'id': 'A', 'height': 0.0}]}
    layout_bytes = json.dumps(layout).encode('utf-8')
    assets = Path(project) / 'Assets/SkyIsland'
    layout_path = assets / 'sky_island_layout.json'
    policy_path = Path(project) / 'Assets/Editor/SkyIslandCollisionBuilder.cs'
    policy_text = policy_path.read_text(encoding='utf-8-sig')
    original_read_text, original_read_bytes = Path.read_text, Path.read_bytes

    def export(rows):
        writes = []
        def read_text(path, *args, **kwargs):
            return layout_bytes.decode() if path == layout_path else original_read_text(path, *args, **kwargs)
        def write_text(path, text, *args, **kwargs):
            writes.append(json.loads(text))
            return len(text)
        with patch.dict(sys.modules, {'bpy': SimpleNamespace(data=SimpleNamespace(objects=rows))}), \
                patch.object(Path, 'read_text', read_text), patch.object(Path, 'write_text', write_text):
            exporter = importlib.import_module('export_sky_island_collision_footprints')
            # A cached module must query this invocation's bpy boundary too.
            with patch.object(exporter, 'bpy', sys.modules['bpy']):
                exporter.export(project, layout_path, assets / 'sky_island_collision_footprints.json')
        require(len(writes) == 1, 'EXPORT_WRITES', len(writes))
        return writes[0]

    exported = export(objects)
    require(exported['signatureSolids'] == sorted((island, bridge)), 'EXPORT_SIGNATURE_SOURCES', exported)
    require(exported['sliceSolids'] == [island] and [r['name'] for r in exported['objects']] == [island],
            'EXPORT_SLICE_SOURCES', exported)
    baseline = {'sourceSolids': exported['signatureSolids'], 'sourceCollisionSha256': exported['sourceCollisionSha256'],
                'sourceLayoutSha256': hashlib.sha256(layout_bytes).hexdigest(), 'connectedTriangles': 1,
                'navigation': {'vertices': [[0, 0, 0], [1, 0, 0], [0, 0, 1]], 'triangles': [[0, 2, 1]]}}

    def apply(rows, data, generator, writes, current_policy=None):
        sidecar_bytes = json.dumps(data).encode('utf-8')
        def read_text(path, *args, **kwargs):
            if path == SIDECAR:
                return sidecar_bytes.decode()
            if path == policy_path and current_policy is not None:
                return current_policy
            return original_read_text(path, *args, **kwargs)
        def read_bytes(path):
            if path == layout_path:
                return layout_bytes
            if path == SIDECAR:
                return sidecar_bytes
            return original_read_bytes(path)
        def write_bytes(path, data):
            writes.append((path, data))
            return len(data)
        with patch.dict(sys.modules, {'bpy': SimpleNamespace(data=SimpleNamespace(objects=rows))}), \
                patch.object(Path, 'read_text', read_text), patch.object(Path, 'read_bytes', read_bytes), \
                patch.object(Path, 'write_bytes', write_bytes):
            return production.apply_navigation(generator, layout, assets)

    generator, writes = Mock(), []
    require(apply(objects, baseline, generator, writes) == baseline['navigation'], 'VALID_SOURCE_REJECTED', baseline)
    generator.create_object.assert_called_once()
    require(len(writes) == 1, 'VALID_NAV_COPY', writes)
    # Enumeration order must not change either the source set or its signature.
    production.validate_collision_sources(baseline, dict(reversed(list(objects.items()))), project)
    negatives = []
    def reject(label, rows, expected, data=None, current_policy=None):
        generator, writes = Mock(), []
        try:
            apply(rows, baseline if data is None else data, generator, writes, current_policy)
        except ValueError as error:
            require(expected in str(error), 'WRONG_FINGERPRINT_FAILURE', (label, str(error), expected))
        else:
            raise AssertionError('NEGATIVE_DID_NOT_FAIL: ' + label)
        require(not generator.mock_calls and not writes, 'STALE_NAV_MUTATED', label)
        negatives.append(label)

    changed = copy.deepcopy(objects)
    changed['VIS_A_NewStone'] = memory_mesh('VIS_A_NewStone', ['Sky_Rock'])
    reject('new_hard_mesh', changed, 'Solid source set changed')
    changed = copy.deepcopy(objects); del changed[island]
    reject('deleted_hard_mesh', changed, 'Solid source set changed')
    changed = copy.deepcopy(objects); changed[soft].data.materials[0].name = 'Sky_Rock'
    reject('soft_becomes_hard', changed, 'Solid source set changed')
    changed = copy.deepcopy(objects); changed[island].data.materials[0].name = 'Sky_Leaf'
    reject('hard_becomes_soft', changed, 'Solid source set changed')
    require(policy_text.count('"Leaf",') == 1, 'POLICY_MUTATION_ANCHOR', 'Leaf')
    reject('author_policy_becomes_hard', objects, 'Solid source set changed',
           current_policy=policy_text.replace('"Leaf",', '', 1))
    changed = copy.deepcopy(objects); changed[bridge].data.vertices[0].co.x += 0.25
    changed_export = export(changed)
    require(changed_export['sourceCollisionSha256'] != exported['sourceCollisionSha256']
            and changed_export['objects'] == exported['objects'], 'BRIDGE_SIGNATURE_WITHOUT_RESLICING', changed_export)
    reject('bridge_vertices_changed', changed, 'Solid geometry changed')
    changed = copy.deepcopy(objects); changed[bridge].data.loop_triangles[0].vertices = (0, 2, 1)
    reject('bridge_faces_changed', changed, 'Solid geometry changed')
    legacy = copy.deepcopy(baseline); legacy['sourceSolids'] = [island]
    legacy['sourceCollisionSha256'] = production.collision_signature(objects, [island])
    reject('legacy_island_only_signature', objects, 'Solid source set changed', data=legacy)
    changed = copy.deepcopy(objects); changed[island].data.materials = []
    reject('missing_material_slots', changed, 'Visual has no collision material classification')
    changed = copy.deepcopy(objects); changed[island].data.materials = [None]
    reject('null_material', changed, 'Missing collision material')
    changed = copy.deepcopy(objects); changed[island].data.materials.append(SimpleNamespace(name='Sky_Leaf'))
    reject('mixed_materials', changed, 'Mixed solid/soft collision group')
    return {'baselineAccepted': True, 'bridgeIncludedInSignature': True, 'islandOnlySlices': True,
            'staleNavigationNotWritten': True, 'negativeMutationsRejected': negatives}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--fingerprint-only', action='store_true', help='Only run in-memory production fingerprint regressions')
    args = parser.parse_args()
    sys.path.insert(0, str(ROOT / 'tools'))
    from unity_project_path import find_unity_project
    source_only = os.environ.get('BOSSRUSH_GUARD_SOURCE_ONLY') == '1'
    project = None if source_only else find_unity_project()
    if not source_only:
        require(project is not None, 'AUTHOR_PROJECT_MISSING', 'Set BOSSRUSH_UNITY_PROJECT')
        require((Path(project) / 'Assets/Editor/SkyIslandCollisionBuilder.cs').is_file(),
                'AUTHOR_POLICY_MISSING', 'SkyIslandCollisionBuilder.cs is required in complete mode')
    watched = [SIDECAR, LAYOUT, ROOT / 'SkyIsland/SkyIslandGates.cs', Path(__file__),
               ROOT / 'tools/sky_island_collision_navigation.py', ROOT / 'tools/export_sky_island_collision_footprints.py',
               ROOT / 'tools/build_sky_island_collision_navigation.py']
    if project is not None:
        watched.append(Path(project) / 'Assets/Editor/SkyIslandCollisionBuilder.cs')
    before = {p: hashlib.sha256(p.read_bytes()).hexdigest() for p in watched}
    try:
        if source_only:
            fingerprint_report = {'status': 'PARTIAL',
                                  'reason': 'source-only; real author policy fingerprint regression not executed'}
        else:
            fingerprint_report = verify_fingerprints(project)
            fingerprint_report['status'] = 'PASS'
        if args.fingerprint_only:
            report = {'status': 'PASS', 'failures': [], 'geometryRegression': 'NOT_RUN (--fingerprint-only)'}
        else:
            sidecar = json.loads(SIDECAR.read_text(encoding='utf-8-sig'))
            layout = json.loads(LAYOUT.read_text(encoding='utf-8-sig'))
            require(sidecar.get('version') == 1, 'VERSION', sidecar.get('version'))
            require(sidecar.get('sourceLayoutSha256') == before[LAYOUT], 'LAYOUT_FINGERPRINT', sidecar.get('sourceLayoutSha256'))
            fingerprint = sidecar.get('sourceCollisionSha256', '')
            require(isinstance(fingerprint, str) and len(fingerprint) == 64
                    and all(c in string.hexdigits for c in fingerprint), 'COLLISION_FINGERPRINT_FORMAT', fingerprint)
            solids = sidecar.get('sourceSolids', [])
            require(solids and all(isinstance(s, str) and s.startswith('VIS_') for s in solids)
                    and len(solids) == len(set(solids)), 'SOLID_SOURCE_LIST', len(solids))
            gates = gate_test.read_gates()
            report, adapted = check(sidecar, layout, gates)
            negatives = []
            broken = copy.deepcopy(sidecar)
            broken['navigation']['vertices'].extend([[0, 0, 0]] * 4095)
            negatives.append(expect_red('budget', lambda: topology(broken, layout), 'BUDGET'))
            broken = copy.deepcopy(sidecar)
            nav = broken['navigation']
            # Duplicate only AB's shore indices: geometry remains identical, true common-edge connectivity breaks.
            island_indices = {v for face, r in zip(nav['triangles'], nav['triangleRegions'])
                              if r in ('A', 'B') for v in face}
            bridge_indices = {v for face, r in zip(nav['triangles'], nav['triangleRegions']) if r == 'AB' for v in face}
            replacement = {}
            for i in sorted(island_indices & bridge_indices):
                replacement[i] = len(nav['vertices']); nav['vertices'].append(list(nav['vertices'][i]))
            require(replacement and len(nav['vertices']) < 4095, 'NEGATIVE_SETUP', 'AB seam')
            for face, r in zip(nav['triangles'], nav['triangleRegions']):
                if r == 'AB':
                    face[:] = [replacement.get(i, i) for i in face]
            negatives.append(expect_red('bridge_connection', lambda: topology(broken, layout), 'CONNECTED'))
            broken = copy.deepcopy(sidecar)
            nav = broken['navigation']
            face = next(f for f, r in zip(nav['triangles'], nav['triangleRegions']) if r == 'AB')
            nav['vertices'][face[0]][1] += 1.0
            negatives.append(expect_red('bridge_height', lambda: verify_support(topology(broken, layout), layout), 'HEIGHT'))
            damaged_gates = copy.deepcopy(gates)
            damaged_gates['K1']['position'][0] += 1000.0
            negatives.append(expect_red('gate_off_bridge', lambda: gate_test.verify(adapted, damaged_gates), 'K1'))
            report['negativeMutationsRejected'] = negatives
            report['status'] = 'FAIL' if report['failures'] else 'PASS'
            report['geometryRegression'] = report['status']
        if source_only and report['status'] == 'PASS':
            report['status'] = 'PARTIAL'
        report['fingerprintRegression'] = fingerprint_report
    finally:
        for path, digest in before.items():
            require(hashlib.sha256(path.read_bytes()).hexdigest() == digest, 'SOURCE_BYTES_CHANGED', str(path))
    report['sourceBytesUnchanged'] = True
    report['inputSha256'] = {str(path): digest for path, digest in before.items()}
    print('SkyIslandCollisionNavigationPropertyTest: ' + report['status'])
    print(json.dumps(report, ensure_ascii=False, indent=2))
    require(not report['failures'], 'MARKER_APPROACH', report['failures'])
    if source_only:
        print('SkyIslandCollisionNavigationPropertyTest: PARTIAL (real author policy fingerprint regression not verified)')
        return 2
    return 0

if __name__ == '__main__':
    raise SystemExit(main())
