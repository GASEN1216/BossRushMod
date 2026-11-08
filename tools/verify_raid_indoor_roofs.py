"""Read the *shipped* scene bundles; compare indoor volumes, roof membership and layout (L2, no game/slot)."""
import argparse
import hashlib
import itertools
import json
import math
from pathlib import Path

import UnityPy
import numpy as np
from UnityPy.helpers.MeshHelper import MeshHandler

ROOT = Path(__file__).resolve().parents[1]


class Scene:
    def __init__(self, path):
        self.env = UnityPy.load(str(path))
        self.nodes, self.named = {}, {}
        for obj in self.env.objects:
            if obj.type.name != "Transform":
                continue
            t = obj.read()
            g = t.m_GameObject.read()
            self.nodes[obj.path_id] = (g, t)
            self.named.setdefault(g.m_Name, []).append(obj.path_id)

    def children(self, root):
        return [p.path_id for p in self.nodes[root][1].m_Children]

    def descendants(self, root):
        yield root
        for t in self.children(root):
            yield from self.descendants(t)

    def components(self, root, kind):
        return [c.component for t in self.descendants(root) for c in self.nodes[t][0].m_Component
                if c.component.type.name == kind]

    def unique(self, name):
        vals = self.named[name]
        assert len(vals) == 1, (name, vals)
        return vals[0]

    def indexed(self, name):
        children = self.children(self.unique(name))
        result = {self.nodes[t][0].m_Name: t for t in children}
        assert len(result) == len(children)
        return result


def close(left, right):
    return all(math.isclose(x, y, abs_tol=.002) for x, y in zip(left, right))


def xyz(v):
    return (v.x, v.y, v.z)


def footprint(s, node, center, size):
    g, t = s.nodes[node]
    colliders = [r.component for r in g.m_Component if r.component.type.name == 'BoxCollider']
    assert len(colliders) == 1, g.m_Name
    box = colliders[0].read()
    assert (g.m_Layer == 2 and box.m_Enabled and box.m_IsTrigger and close(xyz(box.m_Center), (0, 0, 0))
            and close(xyz(box.m_Size), size) and close(xyz(t.m_LocalPosition), center)), (g.m_Name, center, size)


def world_matrix(scene, node):
    t = scene.nodes[node][1]
    q = t.m_LocalRotation
    x, y, z, w = q.x, q.y, q.z, q.w
    rotation = np.array([[1-2*(y*y+z*z), 2*(x*y-z*w), 2*(x*z+y*w)],
                         [2*(x*y+z*w), 1-2*(x*x+z*z), 2*(y*z-x*w)],
                         [2*(x*z-y*w), 2*(y*z+x*w), 1-2*(x*x+y*y)]])
    matrix = np.eye(4)
    matrix[:3, :3] = rotation @ np.diag(xyz(t.m_LocalScale))
    matrix[:3, 3] = xyz(t.m_LocalPosition)
    return world_matrix(scene, t.m_Father.path_id) @ matrix if t.m_Father.path_id else matrix


def clipped_polygon(triangle, height, above):
    output = []
    for i, a in enumerate(triangle):
        b = triangle[(i + 1) % 3]
        keep_a = a[1] >= height if above else a[1] <= height
        keep_b = b[1] >= height if above else b[1] <= height
        if keep_a:
            output.append(a)
        if keep_a != keep_b:
            output.append(a + (b-a) * ((height-a[1]) / (b[1]-a[1])))
    return np.asarray(output)


def area_moment(polygon):
    area, moment = 0.0, np.zeros(3)
    for i in range(1, len(polygon)-1):
        tri = polygon[[0, i, i+1]]
        part = np.linalg.norm(np.cross(tri[1]-tri[0], tri[2]-tri[0])) / 2
        area += part
        moment += part * tri.mean(axis=0)
    return area, moment


def packed_triangles(scene, root, origin):
    for node in scene.descendants(root):
        for ref in scene.nodes[node][0].m_Component:
            if ref.component.type.name != 'MeshFilter':
                continue
            mesh = MeshHandler(ref.component.read().m_Mesh.read())
            mesh.process()
            matrix = world_matrix(scene, node)
            vertices = np.asarray(mesh.m_Vertices) @ matrix[:3, :3].T + matrix[:3, 3]
            vertices[:, 2] *= -1
            vertices -= origin
            for indices in mesh.get_triangles():
                yield from vertices[np.asarray(indices, dtype=int)]


def semantic_roof_faces(scene, layout):
    """Validate the visible indoor result: low structural walls, whole furniture, no floating upper shell."""
    raw = json.loads((ROOT / 'ArtSource/Tidegate/fbx/tidegate_roof_faces.json').read_text('utf-8'))
    source = ROOT / 'docs/design/tidegate-town/v2/js/kit-buildings.js'
    assert raw['schema'] == 2 and raw['lowWallHeight'] == .9, 'Stale indoor ownership data'
    if source.exists():
        assert hashlib.sha256(source.read_bytes()).hexdigest() == raw['sourceSha256'], 'Indoor ownership source changed; regenerate semantic data'
    cut = raw['lowWallHeight']
    totals = dict(Roof=0, LowWalls=0, Contents=0)
    for b in layout['buildings']:
        name = b['id']
        source_groups = raw['buildings'][name]
        polygons = dict(Roof=[], LowWalls=[], Contents=[])
        polygons['Roof'].extend(np.asarray(t).reshape(3, 3) for t in source_groups['upper'])
        polygons['Contents'].extend(np.asarray(t).reshape(3, 3) for t in source_groups['contents'])
        for t in source_groups['structure']:
            tri = np.asarray(t).reshape(3, 3)
            if tri[:, 1].max() <= cut + .00001:
                polygons['LowWalls'].append(tri)
            elif tri[:, 1].min() >= cut - .00001:
                polygons['Roof'].append(tri)
            else:
                polygons['LowWalls'].append(clipped_polygon(tri, cut, False))
                polygons['Roof'].append(clipped_polygon(tri, cut, True))
        x0, z0, x1, z1 = b['footprint']
        origin = np.array([(x0+x1)/2, b['floorY'], (z0+z1)/2])
        covered_renderers = set()
        for suffix, expected in polygons.items():
            root = scene.unique('BLD_' + name + '_' + suffix)
            covered_renderers.update(p.path_id for p in scene.components(root, 'MeshRenderer'))
            buckets, candidates, targets = {}, [], []
            for source_id, polygon in enumerate(expected):
                area, moment = area_moment(polygon)
                targets.append((area, moment))
                # A clipped quad may use either diagonal after the FBX importer changes triangle winding.
                for indices in itertools.combinations(range(len(polygon)), 3):
                    tri = polygon[list(indices)]
                    if area_moment(tri)[0] < 1e-12:
                        continue
                    candidate_id = len(candidates)
                    candidates.append((source_id, tri))
                    buckets.setdefault(tuple(np.floor(tri.mean(0)*20).astype(int)), []).append(candidate_id)
            used_area = np.zeros(len(expected)); used_moment = np.zeros((len(expected), 3))
            count, max_height = 0, -float('inf')
            for triangle in packed_triangles(scene, root, origin):
                count += 1
                max_height = max(max_height, triangle[:, 1].max())
                if suffix == 'LowWalls':
                    assert max_height <= cut + .001, (name, 'high wall remains visible indoors', max_height)
                area, moment = area_moment(triangle)
                if area < 1e-12:
                    continue
                center = triangle.mean(0)
                lo = np.floor((center-.0003)*20).astype(int)
                hi = np.floor((center+.0003)*20).astype(int)
                matched = None
                for cell in itertools.product(*(range(lo[k], hi[k]+1) for k in range(3))):
                    for candidate_id in buckets.get(cell, ()):
                        source_id, wanted = candidates[candidate_id]
                        if used_area[source_id] + area > targets[source_id][0] + .0002:
                            continue
                        distances = np.abs(triangle[:, None, :] - wanted[None, :, :]).max(axis=2)
                        if distances.min(axis=0).max() < .0003 and distances.min(axis=1).max() < .0003:
                            matched = source_id
                            break
                    if matched is not None:
                        break
                assert matched is not None, (name, suffix, 'unexpected or duplicated indoor geometry', triangle.tolist())
                used_area[matched] += area; used_moment[matched] += moment
            target_area = np.asarray([t[0] for t in targets])
            target_moment = np.asarray([t[1] for t in targets])
            assert np.allclose(used_area, target_area, atol=.0002, rtol=.0002), (name, suffix, 'missing surfaces', float(np.abs(used_area-target_area).max()))
            # Compare centroids in metres: an absolute first-moment epsilon incorrectly tightens
            # positional precision with face area (e.g. the bank's 40.5 m2 floor triangles).
            nonzero = target_area > 1e-12
            assert (used_area[nonzero] > 0).all(), (name, suffix, 'missing small surface')
            used_centroid = used_moment[nonzero] / used_area[nonzero, None]
            target_centroid = target_moment[nonzero] / target_area[nonzero, None]
            assert np.allclose(used_centroid, target_centroid, atol=.0003, rtol=0), (name, suffix, 'surface coverage differs')
            if suffix == 'LowWalls':
                assert max_height >= cut - .002, (name, 'low wall lost')
            if suffix == 'Contents':
                expected_max = max(max(np.asarray(t)[[1, 4, 7]]) for t in source_groups['contents'])
                assert abs(max_height-expected_max) < .001, (name, 'furniture clipped', max_height, expected_max)
                assert count == len(source_groups['contents']), (name, 'furniture triangle count changed', count, len(source_groups['contents']))
            assert count > 0, (name, suffix, 'missing indoor group')
            totals[suffix] += count
        parent = scene.unique('BLD_' + name + '_' + b['style'])
        all_renderers = {p.path_id for p in scene.components(parent, 'MeshRenderer')}
        assert covered_renderers == all_renderers, (name, 'unowned renderer may float indoors')
    print(f"Tidegate indoor geometry: {totals['Roof']} hidden upper faces; {totals['LowWalls']} low-wall faces <= {cut} m; {totals['Contents']} original floor/furniture faces intact")


def tide(layout, path):
    s = Scene(path)
    rooms = s.indexed("TidegateIndoorRooms")
    assert set(rooms) == {b['id'] for b in layout['buildings']}
    renderers = 0
    for b in layout['buildings']:
        name = b['id']
        x0, z0, x1, z1 = b['footprint']
        footprint(s, rooms[name], ((x1+x0)/2, b['floorY']+1.5, -(z1+z0)/2), (x1-x0, 4, z1-z0))
        roof = s.unique('BLD_'+name+'_Roof')
        refs = s.components(roof, 'MeshRenderer')
        assert refs, 'Indoor upper shell missing '+name
        assert all(r.read().m_CastShadows for r in refs), name
        renderers += len(refs)
    # Runtime now only switches the roof renderers with official ShadowOnly. Reject a stale shader
    # bundle that can still dither exterior buildings, cut ground-floor walls, or fade a roof outdoors.
    shaders = [o.read().m_ParsedForm for o in s.env.objects if o.type.name == 'Shader']
    env = [shader for shader in shaders if shader.m_Name == 'BossRush/Tidegate/Environment']
    assert len(env) == 1, 'Environment shader missing/duplicated'
    properties = {prop.m_Name for prop in env[0].m_PropInfo.m_Props}
    obsolete = properties & {'_TidegateExterior', '_TidegateRuin', '_TidegateRoofBackAt'}
    assert not obsolete, ('Stale indoor shader: rebuild tidegate_raid', sorted(obsolete))
    semantic_roof_faces(s, layout)
    print(f'Tidegate: {len(rooms)} original footprints, {renderers} upper-building renderers, low walls/furniture checked')


def negative_semantic(layout, path):
    # Modify only decoded scene relationships in memory; never write the packed resource.
    scene = Scene(path)
    name = layout['buildings'][0]['id']
    root = lambda suffix: scene.nodes[scene.unique('BLD_' + name + '_' + suffix)][1]
    roof, low, contents = root('Roof'), root('LowWalls'), root('Contents')
    upper_child = next(p for p in roof.m_Children if scene.components(p.path_id, 'MeshFilter'))
    furniture_child = next(p for p in contents.m_Children if scene.components(p.path_id, 'MeshFilter'))
    for target, child, expected in ((low, upper_child, 'high wall remains visible indoors'),
                                    (contents, upper_child, 'unexpected or duplicated indoor geometry'),
                                    (roof, furniture_child, 'unexpected or duplicated indoor geometry')):
        target.m_Children.append(child)
        try:
            semantic_roof_faces(scene, layout)
        except AssertionError as err:
            assert expected in str(err), err
        else:
            raise AssertionError('bad indoor ownership went unnoticed: ' + expected)
        finally:
            target.m_Children.pop()
    print('Negative: high wall left visible, floating upper equipment and hidden furniture correctly rejected')


def sky(layout, path):
    s = Scene(path)
    volumes = s.indexed("SkyPavilionRooms")
    assert set(volumes) == {'pavilion0', 'pavilion1'}
    supports = sorted((b for b in layout['obstacles'] if b['id'].startswith('F_PavilionSupport')), key=lambda b:b['id'])
    assert len(supports) == 8
    for r, label in enumerate(sorted(volumes)):
        bs = supports[4*r:4*r+4]
        xmin, xmax = min(b['center'][0] for b in bs), max(b['center'][0] for b in bs)
        zmin, zmax = min(b['center'][2] for b in bs), max(b['center'][2] for b in bs)
        floor = bs[0]['center'][1]-bs[0]['size'][1]/2
        footprint(s, volumes[label], ((xmin+xmax)/2, floor+1.5, (zmax+zmin)/2), (xmax-xmin+1.2,4,zmax-zmin+1.2))
        roof = s.unique('BLD_'+label+'_Roof')
        assert len(s.components(roof, 'MeshRenderer')) == 2
        bound = {f.read().m_Mesh.read().m_Name for f in s.components(roof, 'MeshFilter')}
        assert bound == {'SkyRoom_VIS_F_Teal_'+str(r+1), 'SkyRoom_VIS_F_Brass_'+str(r+1)}, bound
    print('SkyIsland: both four-column pavilions keep separate tile/brass roofs and their own exact footprints')


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--negative-check', action='store_true')
    parser.add_argument('--bundle', type=Path, default=ROOT/'Assets/arenas/tidegate_raid')
    args = parser.parse_args()
    tg = json.loads((ROOT/'ArtSource/Tidegate/export/tidegate_layout.json').read_text('utf-8'))
    path = args.bundle
    tide(tg, path)
    if args.negative_check:
        negative_semantic(tg, path)
        # Enlarging the warehouse towards its neighbouring room must be rejected by actual packed geometry.
        next(b for b in tg['buildings'] if b['id']=='wh1')['footprint'][2] += 9
        try:
            tide(tg, path)
        except AssertionError as err:
            assert 'wh1' in str(err), err
        else:
            raise AssertionError('enlarged warehouse went unnoticed')
        print('Negative: 9 m warehouse encroachment correctly rejected')
    sky(json.loads((ROOT/'ArtSource/SkyIsland/layout.json').read_text('utf-8-sig')), ROOT/'Assets/arenas/sky_island_raid')


if __name__ == '__main__':
    main()
