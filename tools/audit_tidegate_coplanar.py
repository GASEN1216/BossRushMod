"""Audit horizontal coplanar surfaces in the actual Tidegate bundle (L2).

Reports differing-material intersections of positive area and tests a vertical
representative point against all visual triangles. Covered points are excluded.
This is not a camera/GPU visibility proof; sloped/vertical faces, transparent
sorting, shadows and unobserved parts of an intersection need separate checks.
"""
import argparse
import json
from collections import Counter
from pathlib import Path
import sys

import numpy as np
import shapely as sh
from shapely import STRtree
import UnityPy
from UnityPy.helpers.MeshHelper import MeshHandler
from rebuild_lastshift_bundle_layers import transform


def normal(tri):
    return np.cross(tri[:, 1] - tri[:, 0], tri[:, 2] - tri[:, 0])


def shapes(tri):
    return sh.polygons(tri[:, :, [0, 2]])


def heights(tri, xz):
    n = normal(tri)
    return tri[:, 0, 1] - (n[:, 0] * (xz[:, 0] - tri[:, 0, 0]) +
                           n[:, 2] * (xz[:, 1] - tri[:, 0, 2])) / n[:, 1]


def audit(bundle):
    env = UnityPy.load(str(bundle))
    transforms, meshes, visuals = {}, {}, []
    dynamic_surfaces = []
    for obj in env.objects:
        if obj.type.name != 'MeshRenderer':
            continue
        renderer = obj.read()
        go = renderer.m_GameObject.read()
        if not renderer.m_Enabled or not go.m_IsActive:
            continue
        components = {p.component.type.name: p.component for p in go.m_Component}
        if 'MeshFilter' not in components:
            continue
        matrix, path = transform(components['Transform'], transforms)
        if not path.startswith('/TidegateWorld/Visual/'):
            continue
        ref = components['MeshFilter'].read().m_Mesh
        if not ref:
            continue
        key = (str(ref.deref().assets_file.name), ref.path_id)
        if key not in meshes:
            source = ref.read()
            handler = MeshHandler(source)
            handler.process()
            pieces = [np.asarray(t).reshape(-1, 3) + sub.baseVertex
                      for t, sub in zip(handler.get_triangles(), source.m_SubMeshes)]
            meshes[key] = np.asarray(handler.m_Vertices), pieces
        points, pieces = meshes[key]
        world = points @ matrix[:3, :3].T + matrix[:3, 3]
        for indices, material in zip(pieces, renderer.m_Materials):
            if material:
                mat = material.read()
                shader_name = mat.m_Shader.read().m_ParsedForm.m_Name
                if shader_name in ('BossRush/Tidegate/Water', 'BossRush/Tidegate/Beam'):
                    dynamic_surfaces.append(dict(path=path, material=mat.m_Name, shader=shader_name))
                    continue
                visuals.append((path, mat.m_Name, world[indices]))
    if not visuals:
        raise ValueError('No Tidegate visual mesh was found')

    from collections import defaultdict
    batches = defaultdict(list)
    for path, name, raw in visuals:
        n = normal(raw)
        horizontal = (np.abs(n[:, 1]) > 1e-9) & ((np.abs(n[:, 0]) + np.abs(n[:, 2])) < 1e-5 * np.abs(n[:, 1]))
        tri = raw[horizontal]
        if not len(tri) or name == 'TG_Terrain':
            continue
        y = np.round(tri[:, 0, 1] * 10000).astype(np.int64)
        for height in np.unique(y):
            batches[height].append((path, name, tri[y == height]))
    candidates = []
    for y, pieces in sorted(batches.items()):
        tri = np.concatenate([t for _, _, t in pieces])
        owners = [(p, m) for p, m, t in pieces for _ in t]
        poly = shapes(tri)
        a, b = STRtree(poly).query(poly, predicate='intersects')
        keep = a < b
        a, b = a[keep], b[keep]
        keep = np.fromiter((owners[i][1] != owners[j][1] for i, j in zip(a, b)), bool)
        a, b = a[keep], b[keep]
        intersections = sh.intersection(poly[a], poly[b])
        areas = sh.area(intersections)
        for i, j, shape, area in zip(a, b, intersections, areas):
            if area <= 1e-8:
                continue
            p = shape.representative_point()
            candidates.append((float(area), owners[i], owners[j], np.asarray([p.x, y / 10000., p.y])))
    open_pairs = []
    if candidates:
        xz = np.stack([x[-1][[0, 2]] for x in candidates])
        probes = sh.points(xz)
        top = np.full(len(probes), -np.inf)
        for _, _, raw in visuals:
            tri = raw[np.abs(normal(raw)[:, 1]) > 1e-8]
            if not len(tri):
                continue
            a, b = STRtree(shapes(tri)).query(probes, predicate='covered_by')
            np.maximum.at(top, a, heights(tri[b], xz[a]))
        for (area, first, second, xyz), hit in zip(candidates, top):
            if hit <= xyz[1] + .001:
                open_pairs.append(dict(area=area, first=first, second=second, point=xyz.tolist(), top=float(hit)))
    groups = Counter(tuple(sorted((p['first'][1], p['second'][1]))) for p in open_pairs)
    return dict(bundle=str(bundle), horizontalTriangles=sum(len(t) for pieces in batches.values() for _, _, t in pieces),
                dynamicShaderSurfaces=dynamic_surfaces, candidatePairs=len(candidates), openSamples=len(open_pairs), materialPairGroups=len(groups), samples=open_pairs,
                limit='Static horizontal differing-material intersections; dynamic tidal water/beam excluded (shader changes geometry or transparency); one vertical point per intersection, not GPU/camera proof')


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('bundle', type=Path)
    parser.add_argument('--report', type=Path)
    args = parser.parse_args()
    result = audit(args.bundle)
    text = json.dumps(result, ensure_ascii=False, indent=2)
    if args.report:
        args.report.parent.mkdir(parents=True, exist_ok=True)
        args.report.write_text(text + '\n', 'utf-8')
    print(text)
    return int(result['openSamples'] != 0)


if __name__ == '__main__':
    sys.exit(main())
