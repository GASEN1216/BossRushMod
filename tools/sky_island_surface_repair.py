"""天空岛图集模型接缝修复（COMPAT，离线 Blender 工具）。

旧 GLB 常按法线 / UV 拆开几何顶点；直接 COLLAPSE 会分别移动接缝两边，撕开实体。
本工具从原 GLB 恢复位置拓扑，再沿用原预算减面。面角 UV 独立保留，叶片和天然
开口不自动填补。只写显式指定的候选与证据文件，不接入生产、不覆盖原件。

用法：blender --background --factory-startup --python-exit-code 1 --python
tools/sky_island_surface_repair.py -- --source <原.glb> --baseline <原.json>
--output <候选.json> --report <证据.json>
"""

import argparse
from collections import Counter
import copy
import hashlib
import json
from pathlib import Path
import sys

sys.path.insert(0, str(Path(__file__).resolve().parent))


def _sha(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def _corner_signature(mesh):
    """连同所属几何位置校验每个面角；单比 UV 数值分布不能发现错贴到另一面。"""
    uv = mesh.uv_layers.active
    if uv is None:
        raise ValueError('修复要求原图集 UV；不自动展开替代图集。')
    return Counter(
        (tuple(round(x, 5) for x in mesh.vertices[loop.vertex_index].co),
         tuple(round(x, 7) for x in uv.data[loop.index].uv))
        for loop in mesh.loops
    )


def contain_in_placement_bounds(mesh, bounds, maximum_scale_change=0.03):
    """把超界点收进旧净空；不把已在范围内的模型撑大到旧减面碎片的包络。"""
    actual = {
        'min': [min(v[i] for v in mesh['v']) for i in range(3)],
        'max': [max(v[i] for v in mesh['v']) for i in range(3)],
    }
    scale, shift = [], []
    for i in range(3):
        source_span = actual['max'][i] - actual['min'][i]
        contract_span = bounds['max'][i] - bounds['min'][i]
        if source_span <= 0 or contract_span <= 0:
            raise ValueError('包络必须在每一轴都有正跨度。')
        factor = min(1.0, contract_span / source_span)
        if abs(factor - 1) > maximum_scale_change:
            raise ValueError('净空适配需要超过 3% 的非均匀缩放，需先审查形状。')
        low_shift = bounds['min'][i] - actual['min'][i] * factor
        high_shift = bounds['max'][i] - actual['max'][i] * factor
        scale.append(factor)
        shift.append(max(low_shift, min(0.0, high_shift)))
    faces, uvs = copy.deepcopy(mesh['f']), copy.deepcopy(mesh['uv'])
    mesh['v'] = [[max(bounds['min'][i], min(bounds['max'][i], v[i] * scale[i] + shift[i]))
                  for i in range(3)] for v in mesh['v']]
    assert mesh['f'] == faces and mesh['uv'] == uvs
    return {'policy': 'minimum_affine_containment_without_expanding_empty_bounds',
            'actualBoundsBeforeFit': actual, 'fitScale': scale, 'fitTranslation': shift,
            'facesAndAtlasCoordinatesExactlyPreserved': True}


def join_coincident_positions(obj, tolerance=1e-6):
    """只接合微米范围的同位置点；UV 保存在面角，不合并图集坐标。"""
    import bmesh
    before_signature = _corner_signature(obj.data)
    before_vertices = len(obj.data.vertices)
    before_faces = len(obj.data.polygons)
    bm = bmesh.new()
    try:
        bm.from_mesh(obj.data)
        bmesh.ops.remove_doubles(bm, verts=list(bm.verts), dist=tolerance)
        bm.to_mesh(obj.data)
    finally:
        bm.free()
    obj.data.update()
    preserved = before_signature == _corner_signature(obj.data)
    if not preserved or before_faces != len(obj.data.polygons):
        raise ValueError('同位置接合改变原面或面角贴图关系，拒绝输出候选。')
    return {
        'distanceMetres': tolerance,
        'beforeVertices': before_vertices,
        'afterVertices': len(obj.data.vertices),
        'beforeFaces': before_faces,
        'afterFaces': len(obj.data.polygons),
        'cornerPositionAndUvMultisetPreserved': preserved,
    }


def prepare(source_path, baseline_path, output_path, contain_bounds=False):
    import sky_island_tripo_import as importer
    source_path, baseline_path, output_path = map(Path, (source_path, baseline_path, output_path))
    if output_path.resolve() in (source_path.resolve(), baseline_path.resolve()):
        raise ValueError('输出必须使用独立候选路径。')
    source_sha, baseline_sha = _sha(source_path), _sha(baseline_path)
    original = json.loads(baseline_path.read_text(encoding='utf-8-sig'))
    importer.clear_scene()
    obj = importer.import_model(source_path)
    importer.normalise(obj, original['meta']['height'], original['meta'].get('footprint'))
    proof = join_coincident_positions(obj)
    importer.decimate(obj, original['meta']['triangles'])
    candidate = copy.deepcopy(original)
    candidate['mesh'] = importer.extract(obj)
    containment = (contain_in_placement_bounds(candidate['mesh'], original['meta']['bounds'])
                   if contain_bounds else None)
    triangles = len(candidate['mesh']['f'])
    if triangles > original['meta']['triangles']:
        raise ValueError('修复超出原面数预算。')
    candidate['meta'].update({
        'vertices': len(candidate['mesh']['v']), 'triangles': triangles,
        'sceneTriangles': triangles * original['meta']['instances'],
        'actualBounds': {
            'min': [min(v[i] for v in candidate['mesh']['v']) for i in range(3)],
            'max': [max(v[i] for v in candidate['mesh']['v']) for i in range(3)],
        },
        'surfaceRepair': {
            'method': 'coincident_source_positions_joined_before_decimation_preserving_corner_uv',
            'sourceGLBSHA256': source_sha, 'originalJSONSHA256': baseline_sha,
            'joinDistanceMetres': 1e-6, 'originalBoundsRetained': True,
        },
    })
    if containment is not None:
        candidate['meta']['surfaceRepair']['placementFit'] = containment
    output_path.parent.mkdir(parents=True, exist_ok=True)
    output_path.write_text(json.dumps(candidate, ensure_ascii=False), encoding='utf-8')
    unchanged = source_sha == _sha(source_path) and baseline_sha == _sha(baseline_path)
    if not unchanged:
        raise ValueError('运行期间原件发生变化，请重新取样。')
    return {
        'classification': 'COMPAT', 'sourceGLB': str(source_path.resolve()),
        'sourceGLBSHA256': source_sha, 'baselineJSON': str(baseline_path.resolve()),
        'baselineJSONSHA256': baseline_sha, 'candidateJSON': str(output_path.resolve()),
        'candidateSHA256': _sha(output_path), 'sourceFilesUnchanged': unchanged,
        'coincidentJoin': proof, 'trianglesBefore': len(original['mesh']['f']),
        'trianglesAfter': triangles, 'originalMetadataBoundsPreserved':
            candidate['meta']['bounds'] == original['meta']['bounds'],
        'placementContainment': containment,
        'limits': '边界环与实际画面需另行审查；不把叶片或贴地开口自动当作缺陷。',
    }


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    for name in ('source', 'baseline', 'output', 'report'):
        parser.add_argument('--' + name, required=True, type=Path)
    parser.add_argument('--contain-placement-bounds', action='store_true')
    args = parser.parse_args(sys.argv[sys.argv.index('--') + 1:] if '--' in sys.argv else [])
    if args.report.resolve() in {path.resolve() for path in (args.source, args.baseline, args.output)}:
        raise ValueError('证据文件不能覆盖原件或候选模型。')
    report = prepare(args.source, args.baseline, args.output, args.contain_placement_bounds)
    args.report.parent.mkdir(parents=True, exist_ok=True)
    args.report.write_text(json.dumps(report, ensure_ascii=False, indent=2) + '\n', encoding='utf-8')
    print('SURFACE_REPAIR_PASS', report['trianglesBefore'], report['trianglesAfter'], flush=True)


if __name__ == '__main__':
    main()
