"""将本批静态 GLB 直接转换为 Unity 网格输入，保留原始拓扑/UV，不依赖 Blender。

仅接受清单锁定的单节点、单材质、无骨骼 GLB，未知格式 fail closed。
Unity 负责原生 Mesh/材质/prefab、纹理重导入及正式 AssetBundle 构建。
"""
from pathlib import Path
import argparse
import hashlib
import io
import json
import math
import os
import shutil
import struct
import subprocess
from PIL import Image
from unity_project_path import find_unity_project, find_unity_editor

ROOT = Path(__file__).resolve().parents[1]


def read_glb(path):
    blob = path.read_bytes()
    magic, version, length = struct.unpack_from('<III', blob)
    if magic != 0x46546C67 or version != 2 or length != len(blob):
        raise ValueError('Invalid GLB header')
    count, kind = struct.unpack_from('<II', blob, 12)
    if kind != 0x4E4F534A:
        raise ValueError('Missing JSON chunk')
    doc = json.loads(blob[20:20 + count])
    size, kind = struct.unpack_from('<II', blob, 20 + count)
    data = blob[28 + count:28 + count + size]
    if kind != 0x004E4942 or len(data) != size:
        raise ValueError('Missing BIN chunk')
    if (len(doc['nodes']) != 1 or set(doc['nodes'][0]) != {'mesh', 'name'} or
            len(doc['meshes']) != 1 or len(doc['materials']) != 1 or doc.get('skins') or
            doc.get('animations') or doc.get('extensionsRequired')):
        raise ValueError('Expected one static identity node/material')
    primitives = doc['meshes'][0]['primitives']
    if len(primitives) != 1 or primitives[0].get('mode', 4) != 4:
        raise ValueError('Expected one triangle primitive')

    def accessor(index):
        a = doc['accessors'][index]
        view = doc['bufferViews'][a['bufferView']]
        if a.get('sparse') or a.get('normalized') or view.get('byteStride'):
            raise ValueError('Unsupported accessor layout')
        fmt = {5125: 'I', 5123: 'H', 5126: 'f'}[a['componentType']]
        dims = {'SCALAR': 1, 'VEC2': 2, 'VEC3': 3}[a['type']]
        offset = view.get('byteOffset', 0) + a.get('byteOffset', 0)
        byte_count = struct.calcsize(fmt) * a['count'] * dims
        raw = data[offset:offset + byte_count]
        if len(raw) != byte_count or offset + byte_count > view.get('byteOffset', 0) + view['byteLength']:
            raise ValueError('Accessor exceeds buffer')
        return list(struct.iter_unpack('<' + fmt * dims, raw))

    prim = primitives[0]
    positions = accessor(prim['attributes']['POSITION'])
    normals = accessor(prim['attributes']['NORMAL'])
    uv = accessor(prim['attributes']['TEXCOORD_0'])
    indices = [v[0] for v in accessor(prim['indices'])]
    if len(positions) != len(normals) or len(positions) != len(uv) or len(indices) % 3 or max(indices) >= len(positions):
        raise ValueError('Invalid vertex/index counts')
    if not all(math.isfinite(v) for array in (positions, normals, uv) for row in array for v in row):
        raise ValueError('Non-finite mesh value')
    material = doc['materials'][0]
    if material.get('alphaMode', 'OPAQUE') != 'OPAQUE':
        raise ValueError('Transparent source requires separate material review')
    image_id = doc['textures'][material['pbrMetallicRoughness']['baseColorTexture']['index']]['source']
    view = doc['bufferViews'][doc['images'][image_id]['bufferView']]
    image = data[view['byteOffset']:view['byteOffset'] + view['byteLength']]
    return positions, normals, uv, indices, image


def normalize(positions, normals, uv, indices, box):
    # 已目检四件的正面均为 glTF +X；转换至 Unity -Z，Y 保持向上。
    # 反射坐标变换必须同时反转绕序与 UV 的 V，避免背面剔除/倒置贴图。
    points = [(-p[2], p[1], -p[0]) for p in positions]
    lo = [min(v[a] for v in points) for a in range(3)]
    hi = [max(v[a] for v in points) for a in range(3)]
    scale = min(box[a] / (hi[a] - lo[a]) for a in range(3))
    center = [(lo[0] + hi[0]) / 2, lo[1], (lo[2] + hi[2]) / 2]
    vertices = [[(v[a] - center[a]) * scale for a in range(3)] for v in points]
    triangles = [indices[i + k] for i in range(0, len(indices), 3) for k in (0, 2, 1)]
    return {'vertices': vertices, 'normals': [[-v[2], v[1], -v[0]] for v in normals],
            'uv': [[v[0], 1 - v[1]] for v in uv], 'triangles': triangles,
            'boundsSize': [(hi[a] - lo[a]) * scale for a in range(3)]}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--output', type=Path, required=True)
    parser.add_argument('--build', action='store_true')
    args = parser.parse_args()
    project = find_unity_project()
    if not project:
        raise SystemExit('Unity author project missing')
    project = Path(project)
    output = args.output.resolve()
    output.mkdir(parents=True, exist_ok=True)
    profile = json.loads((ROOT / 'tools/building_model_manifest.json').read_text(encoding='utf8'))
    rows = []
    for entry in profile['buildings']:
        source = ROOT / 'output/building_concepts' / entry['source']
        if hashlib.sha256(source.read_bytes()).hexdigest() != entry['sha256']:
            raise ValueError('Source SHA changed: ' + str(source))
        pos, normals, uv, indices, image = read_glb(source)
        if len(indices) // 3 > profile['maxTriangles']:
            raise ValueError('Triangle budget exceeded')
        mesh = normalize(pos, normals, uv, indices, entry['box'])
        target = project / 'Assets/BaseBuildings' / entry['id']
        target.mkdir(parents=True, exist_ok=True)
        (target / 'mesh.json').write_text(json.dumps(mesh, separators=(',', ':')), encoding='utf8')
        with Image.open(io.BytesIO(image)) as texture:
            original_size = texture.size
            texture.convert('RGB').resize((profile['textureSize'], profile['textureSize']), Image.Resampling.LANCZOS).save(target / 'Albedo.png')
        rows.append(dict(entry, vertices=len(pos), triangles=len(indices)//3, bounds=mesh['boundsSize'], originalTextureSize=original_size))
    (output / 'import-report.json').write_text(json.dumps(rows, indent=2), encoding='utf8')
    shutil.copyfile(ROOT / 'tools/building_model_manifest.json', project / 'Assets/BaseBuildings/manifest.json')
    shutil.copyfile(ROOT / 'tools/BaseBuildingBundleBuilder.cs.txt', project / 'Assets/Editor/BaseBuildingBundleBuilder.cs')
    legacy = project / 'Assets/Editor/PetNestBuildingBundleBuilder.cs'
    original = legacy.read_bytes()
    text = original.decode('utf8')
    newline = '\r\n' if '\r\n' in text else '\n'
    text = text.replace('\r\n', '\n')
    begin = text.index('    public static void BuildOnly()')
    end = text.index('    public static void BuildOnlyAndExit()', begin)
    updated = text[:begin] + '    public static void BuildOnly()\n    {\n        BaseBuildingBundleBuilder.BuildPetNestOnly();\n    }\n\n' + text[end:]
    encoded = updated.replace('\n', newline).encode('utf8')
    if encoded != original:
        backup = output / 'legacy-builder-backup.cs.txt'
        if not backup.exists():
            backup.write_bytes(original)
        legacy.write_bytes(encoded)
    if args.build:
        editor = find_unity_editor()
        if not editor:
            raise SystemExit('Unity Editor missing')
        env = dict(os.environ, BOSSRUSH_BUILDING_OUTPUT=str(output))
        return subprocess.call([editor, '-batchmode', '-force-d3d11', '-projectPath', str(project),
            '-executeMethod', 'BaseBuildingBundleBuilder.BuildAndExit', '-logFile', str(output / 'unity-build.log')], env=env)
    return 0


if __name__ == '__main__':
    raise SystemExit(main())
